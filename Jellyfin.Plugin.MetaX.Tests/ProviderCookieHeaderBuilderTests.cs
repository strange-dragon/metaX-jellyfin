using System.Text.Json;
using Jellyfin.Plugin.MetaX;
using Jellyfin.Plugin.MetaX.Configuration;

namespace Jellyfin.Plugin.MetaX.Tests;

public class ProviderCookieHeaderBuilderTests
{
    [Fact]
    public void Build_FiltersCookiesByCategory()
    {
        var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["JavDB"] = "javdb=1",
            ["FANZA"] = "fanza=1"
        };

        var result = ProviderCookieHeaderBuilder.Build(
            cookies,
            "censored",
            Routes(("censored", "JavDB")),
            null);

        using var json = JsonDocument.Parse(result.HeaderValue!);
        Assert.Single(json.RootElement.EnumerateObject());
        Assert.Equal("javdb=1", json.RootElement.GetProperty("JavDB").GetString());
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Build_MapsSingleTargetAliasesToManifestNames()
    {
        var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DMM"] = "dmm=1",
            ["MGStage"] = "mgs=1"
        };

        var result = ProviderCookieHeaderBuilder.Build(
            cookies,
            "uncensored",
            Routes(("uncensored", "FANZA", "MGS")),
            null);

        using var json = JsonDocument.Parse(result.HeaderValue!);
        Assert.Equal("dmm=1", json.RootElement.GetProperty("FANZA").GetString());
        Assert.Equal("mgs=1", json.RootElement.GetProperty("MGS").GetString());
    }

    [Fact]
    public void Build_KeepsMultiTargetAliasWhenBothProvidersAreAllowed()
    {
        var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ThePornDB"] = "tpd=1"
        };

        var result = ProviderCookieHeaderBuilder.Build(
            cookies,
            "western",
            Routes(("western", "ThePornDBMovie", "ThePornDBScene")),
            null);

        using var json = JsonDocument.Parse(result.HeaderValue!);
        Assert.Equal("tpd=1", json.RootElement.GetProperty("ThePornDB").GetString());
    }

    [Fact]
    public void Build_CollapsesMultiTargetAliasWhenOnlyOneProviderIsAllowed()
    {
        var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ThePornDB"] = "tpd=1"
        };

        var result = ProviderCookieHeaderBuilder.Build(
            cookies,
            "western",
            Routes(("western", "ThePornDBMovie")),
            null);

        using var json = JsonDocument.Parse(result.HeaderValue!);
        Assert.Equal("tpd=1", json.RootElement.GetProperty("ThePornDBMovie").GetString());
    }

    [Fact]
    public void Build_ExplicitProviderWinsOverCategoryScope()
    {
        var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["JavDB"] = "javdb=1",
            ["FANZA"] = "fanza=1"
        };

        var result = ProviderCookieHeaderBuilder.Build(
            cookies,
            "censored",
            Routes(("censored", "JavDB")),
            "FANZA");

        using var json = JsonDocument.Parse(result.HeaderValue!);
        Assert.Equal("fanza=1", json.RootElement.GetProperty("FANZA").GetString());
        Assert.False(json.RootElement.TryGetProperty("JavDB", out _));
    }

    [Fact]
    public void Build_SendsAllConfiguredCookiesForUnscopedRequests()
    {
        var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["JavDB"] = "javdb=1",
            ["FANZA"] = "fanza=1"
        };

        var result = ProviderCookieHeaderBuilder.Build(cookies, category: null, routeProviders: null,
            explicitProvider: null);

        using var json = JsonDocument.Parse(result.HeaderValue!);
        Assert.Equal(2, json.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public void Build_OmitsHeaderWhenManifestIsMissing()
    {
        var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["JavDB"] = "javdb=1"
        };

        var result = ProviderCookieHeaderBuilder.Build(cookies, "censored", routeProviders: null,
            explicitProvider: null);

        Assert.Null(result.HeaderValue);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void Build_RejectsCookieValuesWithNewlinesWithoutLeakingTheValue()
    {
        const string secret = "secret-value";
        var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["JavDB"] = "a=1\r\n" + secret
        };

        var exception = Assert.Throws<ProviderCookieConfigurationException>(() =>
            ProviderCookieHeaderBuilder.Build(cookies, null, null, null));

        Assert.DoesNotContain(secret, exception.Message);
        Assert.Contains("JavDB", exception.Message);
    }

    [Fact]
    public void Build_DoesNotRejectAnOutOfScopeCookieWithNewlines()
    {
        var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["JavDB"] = "javdb=1",
            ["FANZA"] = "bad\r\nvalue"
        };

        var result = ProviderCookieHeaderBuilder.Build(
            cookies,
            "censored",
            Routes(("censored", "JavDB")),
            null);

        using var json = JsonDocument.Parse(result.HeaderValue!);
        Assert.Equal("javdb=1", json.RootElement.GetProperty("JavDB").GetString());
        Assert.False(json.RootElement.TryGetProperty("FANZA", out _));
    }

    [Fact]
    public void Build_RejectsHeadersOverTheHeaderLimit()
    {
        var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["JavDB"] = new string('a', ProviderCookieHeaderBuilder.MaxHeaderBytes + 1)
        };

        var exception = Assert.Throws<ProviderCookieConfigurationException>(() =>
            ProviderCookieHeaderBuilder.Build(cookies, null, null, null));

        Assert.Contains("limit", exception.Message);
        Assert.DoesNotContain(cookies["JavDB"], exception.Message);
    }

    [Fact]
    public void Build_SkipsBlankEntries()
    {
        var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["JavDB"] = "javdb=1",
            ["FANZA"] = " "
        };

        var result = ProviderCookieHeaderBuilder.Build(cookies, null, null, null);

        using var json = JsonDocument.Parse(result.HeaderValue!);
        Assert.Single(json.RootElement.EnumerateObject());
        Assert.Equal("javdb=1", json.RootElement.GetProperty("JavDB").GetString());
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Routes(
        params (string Category, string[] Providers)[] entries)
    {
        return entries.ToDictionary(
            entry => entry.Category,
            entry => (IReadOnlyList<string>)entry.Providers,
            StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Routes(
        params (string Category, string Provider)[] entries)
    {
        return entries.ToDictionary(
            entry => entry.Category,
            entry => (IReadOnlyList<string>)new[] { entry.Provider },
            StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Routes(
        params (string Category, string Provider1, string Provider2)[] entries)
    {
        return entries.ToDictionary(
            entry => entry.Category,
            entry => (IReadOnlyList<string>)new[] { entry.Provider1, entry.Provider2 },
            StringComparer.OrdinalIgnoreCase);
    }
}
