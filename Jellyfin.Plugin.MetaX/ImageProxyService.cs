using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Jellyfin.Plugin.MetaX;

/// <summary>
/// Creates and validates signed, plugin-local image URLs. Jellyfin's Person
/// image downloader does not call IRemoteImageProvider.GetImageResponse, so the
/// local proxy endpoint is used to attach provider cookies for actor images.
/// </summary>
public sealed class ImageProxyService
{
    private const string ProtectorPurpose = "Jellyfin.Plugin.MetaX.ImageProxy.v1";

    private readonly IDataProtector _protector;
    private readonly Func<string> _localApiBaseUrl;

    public ImageProxyService(IDataProtectionProvider dataProtectionProvider, Func<string> localApiBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);
        ArgumentNullException.ThrowIfNull(localApiBaseUrl);

        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        _localApiBaseUrl = localApiBaseUrl;
    }

    public string CreateUrl(string imageUrl, string category)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
            throw new ArgumentException("Image URL is required.", nameof(imageUrl));

        var payload = new ImageProxyPayload
        {
            Url = imageUrl,
            Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim()
        };

        var protectedBytes = _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(payload));
        var token = Base64UrlEncode(protectedBytes);
        var baseUrl = _localApiBaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Jellyfin's local API URL is unavailable.");

        return $"{baseUrl.TrimEnd('/')}/Plugins/MetaX/Images?token={token}";
    }

    public bool TryReadToken(string token, out ImageProxyPayload payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(token))
            return false;

        try
        {
            var bytes = _protector.Unprotect(Base64UrlDecode(token));
            var candidate = JsonSerializer.Deserialize<ImageProxyPayload>(bytes);
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.Url))
                return false;

            payload = candidate;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or CryptographicException)
        {
            return false;
        }
    }

    private static string Base64UrlEncode(byte[] value)
    {
        return Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2:
                base64 += "==";
                break;
            case 3:
                base64 += "=";
                break;
            case 1:
                throw new FormatException("Invalid base64url value.");
        }

        return Convert.FromBase64String(base64);
    }
}

public sealed class ImageProxyPayload
{
    public string Url { get; set; } = string.Empty;

    public string Category { get; set; }
}
