using Microsoft.AspNetCore.DataProtection;

namespace Jellyfin.Plugin.MetaX.Tests;

public class ImageProxyServiceTests
{
    [Fact]
    public void CreateUrl_RoundTripsSignedPayload()
    {
        var service = new ImageProxyService(
            new PassthroughDataProtectionProvider(),
            () => "http://127.0.0.1:8096/");

        var url = service.CreateUrl(
            "http://metax.test/v1/images/primary/JavDB/ABC-123?category=censored",
            "censored");

        Assert.StartsWith("http://127.0.0.1:8096/Plugins/MetaX/Images?token=", url);
        var token = url[(url.IndexOf("token=", StringComparison.Ordinal) + "token=".Length)..];
        Assert.True(service.TryReadToken(token, out var payload));
        Assert.Equal("http://metax.test/v1/images/primary/JavDB/ABC-123?category=censored", payload.Url);
        Assert.Equal("censored", payload.Category);
    }

    [Fact]
    public void CreateUrl_NormalizesEmptyCategory()
    {
        var service = new ImageProxyService(
            new PassthroughDataProtectionProvider(),
            () => "http://127.0.0.1:8096");

        var url = service.CreateUrl("http://metax.test/image", " ");

        var token = url[(url.IndexOf("token=", StringComparison.Ordinal) + "token=".Length)..];
        Assert.True(service.TryReadToken(token, out var payload));
        Assert.Null(payload.Category);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("AAAA")]
    public void TryReadToken_RejectsInvalidTokens(string token)
    {
        var service = new ImageProxyService(
            new PassthroughDataProtectionProvider(),
            () => "http://127.0.0.1:8096");

        Assert.False(service.TryReadToken(token, out _));
    }

    [Fact]
    public void CreateUrl_RejectsMissingImageUrl()
    {
        var service = new ImageProxyService(
            new PassthroughDataProtectionProvider(),
            () => "http://127.0.0.1:8096");

        Assert.Throws<ArgumentException>(() => service.CreateUrl(" ", "censored"));
    }

    private sealed class PassthroughDataProtectionProvider : IDataProtectionProvider
    {
        public IDataProtector CreateProtector(string purpose)
        {
            return new PassthroughProtector();
        }
    }

    private sealed class PassthroughProtector : IDataProtector
    {
        public IDataProtector CreateProtector(string purpose)
        {
            return this;
        }

        public byte[] Protect(byte[] plaintext)
        {
            return plaintext;
        }

        public byte[] Unprotect(byte[] protectedData)
        {
            return protectedData;
        }
    }
}
