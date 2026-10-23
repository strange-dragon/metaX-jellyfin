using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.MetaX;
using Jellyfin.Plugin.MetaX.Configuration;
using Jellyfin.Plugin.MetaX.Metadata;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.MetaX.Tests;

public class MetaXApiClientTests
{
    [Fact]
    public async Task SearchMovieAsync_AddsCategoryAndScopedCookieHeader()
    {
        var handler = new CapturingHandler(_ => JsonResponse("""{"data":[]}"""));
        var api = CreateClient(handler);

        await api.SearchMovieAsync("ABC-123", "", "censored", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://metax.test/v1/movies/search?q=ABC-123&fallback=True&category=censored",
            request.RequestUri!.ToString());
        Assert.Null(request.Headers.Authorization);
        Assert.Equal("""{"JavDB":"javdb=1"}""", GetCookieHeader(request));
    }

    [Fact]
    public async Task SearchMovieAsync_OmitsCookiesWhenCategoryIsNotMapped()
    {
        var handler = new CapturingHandler(_ => JsonResponse("""{"data":[]}"""));
        var api = CreateClient(handler);

        await api.SearchMovieAsync("ABC-123", "", null, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.False(request.Headers.Contains(MetaXApiClient.ProviderCookiesHeaderName));
    }

    [Fact]
    public async Task GetMovieInfoAsync_AddsProviderAndCategoryCookies()
    {
        var handler = new CapturingHandler(_ =>
            JsonResponse("""{"data":{"id":"ABC-123","provider":"JavDB","title":"Example"}}"""));
        var api = CreateClient(handler);

        var movie = await api.GetMovieInfoAsync("JavDB", "ABC-123", "censored", CancellationToken.None);

        Assert.Equal("Example", movie.Title);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://metax.test/v1/movies/JavDB/ABC-123?lazy=True&category=censored",
            request.RequestUri!.ToString());
        Assert.Equal("""{"JavDB":"javdb=1"}""", GetCookieHeader(request));
    }

    [Fact]
    public async Task GetActorInfoAsync_UsesExplicitProviderCookie()
    {
        var handler = new CapturingHandler(_ =>
            JsonResponse("""{"data":{"id":"actor-1","provider":"JavDBActor"}}"""));
        var api = CreateClient(handler);

        await api.GetActorInfoAsync("JavDBActor", "actor-1", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://metax.test/v1/actors/JavDBActor/actor-1?lazy=True",
            request.RequestUri!.ToString());
        Assert.Equal("""{"JavDBActor":"actor-cookie=1"}""", GetCookieHeader(request));
    }

    [Fact]
    public async Task SearchActorAsync_UsesAllCookiesWhenNoProviderIsKnown()
    {
        var handler = new CapturingHandler(_ => JsonResponse("""{"data":[]}"""));
        var api = CreateClient(handler);

        await api.SearchActorAsync("actor", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        var header = GetCookieHeader(request);
        using var json = JsonDocument.Parse(header!);
        Assert.Equal(3, json.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public async Task GetImageResponseAsync_AddsCategoryScopedCookieHeader()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[] { 1, 2, 3 })
        });
        var api = CreateClient(handler);

        using var response = await api.GetImageResponseAsync(
            "http://metax.test/v1/images/primary/MetaX/movie-1?category=western", "western",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("""{"ThePornDB":"tpd=1"}""", GetCookieHeader(request));
    }

    [Fact]
    public async Task TranslateAsync_DoesNotSendProviderCookies()
    {
        var handler = new CapturingHandler(_ =>
            JsonResponse("""{"data":{"from":"en","to":"zh","translated_text":"ok"}}"""));
        var api = CreateClient(handler);

        var result = await api.TranslateAsync("hello", "en", "zh", "Google", null,
            CancellationToken.None);

        Assert.Equal("ok", result.TranslatedText);
        var request = Assert.Single(handler.Requests);
        Assert.False(request.Headers.Contains(MetaXApiClient.ProviderCookiesHeaderName));
    }

    [Fact]
    public async Task GetProviderCatalogAsync_LoadsProvidersAndRouteManifestWithoutCookies()
    {
        var handler = new CapturingHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/v1/providers")
            {
                return JsonResponse("""
                    {"data":{"movie_providers":{"JavDB":{}},"actor_providers":{"JavDBActor":{}}}}
                    """);
            }

            return JsonResponse("""
                {"data":{"route_providers":{"censored":["JavDB"]},"cookie_transport_version":"1"}}
                """);
        });
        var api = CreateClient(handler);
        api.ManifestFetcherOverride = null;

        var catalog = await api.GetProviderCatalogAsync(CancellationToken.None);

        Assert.True(catalog.ManifestAvailable);
        Assert.Equal("1", catalog.CookieTransportVersion);
        Assert.Contains("JavDB", catalog.MovieProviders);
        Assert.Contains("JavDBActor", catalog.ActorProviders);
        Assert.Contains("JavDB", catalog.RouteProviders["censored"]);
        Assert.All(handler.Requests, request =>
            Assert.False(request.Headers.Contains(MetaXApiClient.ProviderCookiesHeaderName)));
    }

    [Fact]
    public async Task GetImageResponseAsync_DoesNotForwardCookiesToThirdPartyHosts()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var api = CreateClient(handler);

        using var response = await api.GetImageResponseAsync(
            "https://images.example.net/cover.jpg", "western", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.False(request.Headers.Contains(MetaXApiClient.ProviderCookiesHeaderName));
    }

    [Fact]
    public async Task BuildProviderCookieHeaderAsync_OmitsHeaderWhenManifestIsUnavailable()
    {
        var api = CreateClient(
            new CapturingHandler(_ => JsonResponse("""{"data":[]}""")),
            manifestFetcher: _ => throw new HttpRequestException("manifest unavailable"));

        var result = await api.BuildProviderCookieHeaderAsync("censored", null, CancellationToken.None);

        Assert.Null(result.HeaderValue);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public async Task ManifestCache_IsInvalidatedWhenServerChanges()
    {
        var config = CreateConfiguration("javdb=1");
        var fetchCount = 0;
        var api = CreateClient(
            new CapturingHandler(_ => JsonResponse("""{"data":[]}""")),
            manifestFetcher: _ =>
            {
                fetchCount++;
                return Task.FromResult(new ProviderManifest
                {
                    RouteProviders = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["censored"] = new[] { "JavDB" }
                    },
                    CookieTransportVersion = "1"
                });
            },
            config: config);

        await api.BuildProviderCookieHeaderAsync("censored", null, CancellationToken.None);
        await api.BuildProviderCookieHeaderAsync("censored", null, CancellationToken.None);
        Assert.Equal(1, fetchCount);

        config.Server = "http://metax-other.test";
        await api.BuildProviderCookieHeaderAsync("censored", null, CancellationToken.None);
        Assert.Equal(2, fetchCount);
    }

    [Fact]
    public void GetPrimaryImageApiUrl_IncludesCategory()
    {
        var api = CreateClient(new CapturingHandler(_ => JsonResponse("""{"data":[]}""")));

        var url = api.GetPrimaryImageApiUrl("JavDB", "ABC-123", 0.5, "HD", "censored");

        Assert.Contains("category=censored", url);
        Assert.Contains("/v1/images/primary/JavDB/ABC-123", url);
    }

    [Fact]
    public async Task TwoClients_DoNotShareCookieState()
    {
        var handlerOne = new CapturingHandler(_ => JsonResponse("""{"data":[]}"""));
        var handlerTwo = new CapturingHandler(_ => JsonResponse("""{"data":[]}"""));
        var apiOne = CreateClient(handlerOne, config: CreateConfiguration("one=1"));
        var apiTwo = CreateClient(handlerTwo, config: CreateConfiguration("two=2"));

        await Task.WhenAll(
            apiOne.SearchMovieAsync("ABC-123", "", "censored", CancellationToken.None),
            apiTwo.SearchMovieAsync("ABC-123", "", "censored", CancellationToken.None));

        Assert.Equal("""{"JavDB":"one=1"}""", GetCookieHeader(Assert.Single(handlerOne.Requests)));
        Assert.Equal("""{"JavDB":"two=2"}""", GetCookieHeader(Assert.Single(handlerTwo.Requests)));
    }

    private static PluginConfiguration CreateConfiguration(string javDbCookie)
    {
        return new PluginConfiguration
        {
            Server = "http://metax.test",
            ProviderCookies = new List<ProviderCookieEntry>
            {
                new() { Provider = "JavDB", Cookie = javDbCookie },
                new() { Provider = "JavDBActor", Cookie = "actor-cookie=1" },
                new() { Provider = "ThePornDB", Cookie = "tpd=1" }
            }
        };
    }

    private static MetaXApiClient CreateClient(
        CapturingHandler handler,
        Func<CancellationToken, Task<ProviderManifest>> manifestFetcher = null,
        PluginConfiguration config = null)
    {
        config ??= CreateConfiguration("javdb=1");

        var api = new MetaXApiClient(
            () => config,
            new HttpClient(handler),
            NullLogger<MetaXApiClient>.Instance,
            TimeSpan.FromMinutes(1));

        api.ManifestFetcherOverride = manifestFetcher ?? (_ => Task.FromResult(new ProviderManifest
        {
            RouteProviders = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["censored"] = new[] { "JavDB" },
                ["western"] = new[] { "ThePornDBMovie", "ThePornDBScene" }
            },
            CookieTransportVersion = "1"
        }));

        return api;
    }

    private static string GetCookieHeader(HttpRequestMessage request)
    {
        Assert.True(request.Headers.TryGetValues(MetaXApiClient.ProviderCookiesHeaderName, out var values));
        return Assert.Single(values!);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }
}
