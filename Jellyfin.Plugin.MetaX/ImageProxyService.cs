using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;

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
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ImageProxyService(IDataProtectionProvider dataProtectionProvider, Func<string> localApiBaseUrl,
        IHttpContextAccessor httpContextAccessor = null)
    {
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);
        ArgumentNullException.ThrowIfNull(localApiBaseUrl);

        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        _localApiBaseUrl = localApiBaseUrl;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Creates a proxy URL for server-side downloads (for example Person
    /// images). The URL points at Jellyfin's local API so the HTTP client can
    /// reach it even when the public hostname is not resolvable internally.
    /// </summary>
    public string CreateUrl(string imageUrl, string category)
    {
        var token = CreateToken(imageUrl, category);
        var baseUrl = _localApiBaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Jellyfin's local API URL is unavailable.");

        return $"{baseUrl.TrimEnd('/')}/Plugins/MetaX/Images?token={token}";
    }

    /// <summary>
    /// Creates a proxy URL for an image that is rendered by the browser (for
    /// example a remote-search thumbnail). Using the current request origin
    /// keeps the image same-origin with Jellyfin, which avoids mixed-content
    /// blocking when the backend is configured with a plain HTTP URL.
    /// </summary>
    public string CreateDisplayUrl(string imageUrl, string category)
    {
        var token = CreateToken(imageUrl, category);
        var request = _httpContextAccessor?.HttpContext?.Request;
        if (request != null)
        {
            var pathBase = request.PathBase.HasValue ? request.PathBase.Value : string.Empty;
            return $"{request.Scheme}://{request.Host}{pathBase}/Plugins/MetaX/Images?token={token}";
        }

        // Remote search always runs in a request, but keep a safe relative
        // fallback for other call sites.
        return $"/Plugins/MetaX/Images?token={token}";
    }

    private string CreateToken(string imageUrl, string category)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
            throw new ArgumentException("Image URL is required.", nameof(imageUrl));

        var payload = new ImageProxyPayload
        {
            Url = imageUrl,
            Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim()
        };

        var protectedBytes = _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(payload));
        return Base64UrlEncode(protectedBytes);
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
