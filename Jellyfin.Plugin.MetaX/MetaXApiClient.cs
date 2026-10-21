using System.Collections.Specialized;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Web;
using Jellyfin.Plugin.MetaX.Configuration;
using Jellyfin.Plugin.MetaX.Metadata;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MetaX;

/// <summary>
/// HTTP client for the MetaX server. Provider cookies are built per request and
/// never stored in the server, logs, or URLs.
/// </summary>
public sealed class MetaXApiClient
{
    public const string ProviderCookiesHeaderName = "X-MetaX-Provider-Cookies";

    private const string ActorInfoApi = "/v1/actors";
    private const string MovieInfoApi = "/v1/movies";
    private const string ActorSearchApi = "/v1/actors/search";
    private const string MovieSearchApi = "/v1/movies/search";
    private const string PrimaryImageApi = "/v1/images/primary";
    private const string ThumbImageApi = "/v1/images/thumb";
    private const string BackdropImageApi = "/v1/images/backdrop";
    private const string TranslateApi = "/v1/translate";
    private const string ProvidersApi = "/v1/providers";
    private const string StatusApi = "/v1/metax/status";

    private readonly Func<PluginConfiguration> _configuration;
    private readonly HttpClient _httpClient;
    private readonly ILogger<MetaXApiClient> _logger;
    private readonly TimeSpan _manifestTtl;
    private readonly SemaphoreSlim _manifestLock = new(1, 1);

    private ProviderManifest _manifest;
    private DateTimeOffset _manifestExpiresAt;
    private bool _manifestAttempted;

    public MetaXApiClient(
        Func<PluginConfiguration> configuration,
        HttpClient httpClient,
        ILogger<MetaXApiClient> logger,
        TimeSpan? manifestTtl = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _manifestTtl = manifestTtl ?? TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Test seam for manifest-dependent cookie scoping. Production code fetches
    /// the manifest from GET /v1/metax/status.
    /// </summary>
    internal Func<CancellationToken, Task<ProviderManifest>> ManifestFetcherOverride { get; set; }

    public static string DefaultUserAgent
    {
        get
        {
            var version = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0.0.0";
            return $"{Plugin.ProviderName}/{version}";
        }
    }

    private PluginConfiguration Configuration => _configuration();

    public string GetPrimaryImageApiUrl(string provider, string id, double position = -1, string badge = default,
        string category = null)
    {
        return ComposeImageApiUrl(PrimaryImageApi, provider, id,
            ratio: Configuration.PrimaryImageRatio, position: position, badge: badge, category: category);
    }

    public string GetPrimaryImageApiUrl(string provider, string id, string url, double position = -1,
        bool auto = false, string badge = default, string category = null)
    {
        return ComposeImageApiUrl(PrimaryImageApi, provider, id, url,
            Configuration.PrimaryImageRatio, position, auto, badge, category);
    }

    public string GetThumbImageApiUrl(string provider, string id, string category = null)
    {
        return ComposeImageApiUrl(ThumbImageApi, provider, id, category: category);
    }

    public string GetThumbImageApiUrl(string provider, string id, string url, double position = -1,
        bool auto = false, string category = null)
    {
        return ComposeImageApiUrl(ThumbImageApi, provider, id, url, position: position, auto: auto, category: category);
    }

    public string GetBackdropImageApiUrl(string provider, string id, string category = null)
    {
        return ComposeImageApiUrl(BackdropImageApi, provider, id, category: category);
    }

    public string GetBackdropImageApiUrl(string provider, string id, string url, double position = -1,
        bool auto = false, string category = null)
    {
        return ComposeImageApiUrl(BackdropImageApi, provider, id, url, position: position, auto: auto,
            category: category);
    }

    public async Task<HttpResponseMessage> GetImageResponseAsync(string url, string category,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", DefaultUserAgent);

        // Image URLs usually point at the configured MetaX server, but never
        // forward provider cookies to a third-party host if one is supplied.
        if (IsMetaXServerUrl(url))
        {
            await AddProviderCookiesAsync(request, category, null, allowUnscopedCookies: true,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _logger.LogDebug("Skipped provider cookies for a non-MetaX image URL.");
        }

        return await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public Task<ActorInfo> GetActorInfoAsync(string provider, string id, CancellationToken cancellationToken)
    {
        return GetActorInfoAsync(provider, id, true /* default */, cancellationToken);
    }

    public Task<ActorInfo> GetActorInfoAsync(string provider, string id, bool lazy,
        CancellationToken cancellationToken)
    {
        var apiUrl = ComposeInfoApiUrl(ActorInfoApi, provider, id, lazy);
        return GetDataAsync<ActorInfo>(apiUrl, true, null, provider, cancellationToken,
            allowUnscopedCookies: true);
    }

    public Task<MovieInfo> GetMovieInfoAsync(string provider, string id, string category,
        CancellationToken cancellationToken)
    {
        return GetMovieInfoAsync(provider, id, true /* default */, category, cancellationToken);
    }

    public Task<MovieInfo> GetMovieInfoAsync(string provider, string id, bool lazy, string category,
        CancellationToken cancellationToken)
    {
        var apiUrl = ComposeInfoApiUrl(MovieInfoApi, provider, id, lazy, category);
        return GetDataAsync<MovieInfo>(apiUrl, true, category, provider, cancellationToken);
    }

    public Task<List<ActorSearchResult>> SearchActorAsync(string q, CancellationToken cancellationToken)
    {
        return SearchActorAsync(q, string.Empty, true /* default */, cancellationToken);
    }

    public Task<List<ActorSearchResult>> SearchActorAsync(string q, string provider,
        CancellationToken cancellationToken)
    {
        return SearchActorAsync(q, provider, true /* default */, cancellationToken);
    }

    public Task<List<ActorSearchResult>> SearchActorAsync(string q, string provider, bool fallback,
        CancellationToken cancellationToken)
    {
        var apiUrl = ComposeSearchApiUrl(ActorSearchApi, q, provider, fallback);
        return GetDataAsync<List<ActorSearchResult>>(apiUrl, true, null, provider, cancellationToken,
            allowUnscopedCookies: true);
    }

    public Task<List<MovieSearchResult>> SearchMovieAsync(string q, string category,
        CancellationToken cancellationToken)
    {
        return SearchMovieAsync(q, string.Empty, true /* default */, category, cancellationToken);
    }

    public Task<List<MovieSearchResult>> SearchMovieAsync(string q, string provider, string category,
        CancellationToken cancellationToken)
    {
        return SearchMovieAsync(q, provider, true /* default */, category, cancellationToken);
    }

    public Task<List<MovieSearchResult>> SearchMovieAsync(string q, string provider, bool fallback,
        string category, CancellationToken cancellationToken)
    {
        var apiUrl = ComposeSearchApiUrl(MovieSearchApi, q, provider, fallback, category);
        return GetDataAsync<List<MovieSearchResult>>(apiUrl, true, category, provider, cancellationToken);
    }

    public Task<TranslationInfo> TranslateAsync(string q, string from, string to, string engine,
        NameValueCollection nv, CancellationToken cancellationToken)
    {
        var apiUrl = ComposeTranslateApiUrl(TranslateApi, q, from, to, engine, nv);
        return GetDataAsync<TranslationInfo>(apiUrl, false, null, null, cancellationToken,
            includeCookies: false);
    }

    public async Task<ProviderCatalog> GetProviderCatalogAsync(CancellationToken cancellationToken)
    {
        var providersTask = GetProvidersAsync(cancellationToken);
        var manifestTask = GetManifestAsync(cancellationToken);
        await Task.WhenAll(providersTask, manifestTask).ConfigureAwait(false);

        var providers = await providersTask.ConfigureAwait(false);
        var manifest = await manifestTask.ConfigureAwait(false);

        var routeProviders = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (category, providersForCategory) in manifest?.RouteProviders ??
                 new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase))
        {
            routeProviders[category] = providersForCategory?.ToList() ?? new List<string>();
        }

        return new ProviderCatalog
        {
            MovieProviders = (providers.MovieProviders?.Keys ?? Enumerable.Empty<string>())
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            ActorProviders = (providers.ActorProviders?.Keys ?? Enumerable.Empty<string>())
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            RouteProviders = routeProviders,
            CookieTransportVersion = manifest?.CookieTransportVersion ?? string.Empty,
            ManifestAvailable = manifest != null
        };
    }

    private async Task<ProvidersInfo> GetProvidersAsync(CancellationToken cancellationToken)
    {
        try
        {
            var apiUrl = ComposeUrl(ProvidersApi, new NameValueCollection());
            return await GetDataAsync<ProvidersInfo>(apiUrl, true, null, null, cancellationToken,
                includeCookies: false).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            _logger.LogWarning("MetaX provider catalog is unavailable: {Message}", e.Message);
            return new ProvidersInfo();
        }
    }

    private async Task<ProviderManifest> GetManifestAsync(CancellationToken cancellationToken)
    {
        if (_manifestAttempted && DateTimeOffset.UtcNow < _manifestExpiresAt)
            return _manifest;

        await _manifestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_manifestAttempted && DateTimeOffset.UtcNow < _manifestExpiresAt)
                return _manifest;

            try
            {
                var manifest = ManifestFetcherOverride != null
                    ? await ManifestFetcherOverride(cancellationToken).ConfigureAwait(false)
                    : await FetchManifestAsync(cancellationToken).ConfigureAwait(false);

                _manifest = manifest;
                _manifestAttempted = true;
                _manifestExpiresAt = DateTimeOffset.UtcNow + _manifestTtl;
                return manifest;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                // Cache failures briefly so a broken manifest cannot cause one
                // extra request per metadata call.
                _manifest = null;
                _manifestAttempted = true;
                _manifestExpiresAt = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
                _logger.LogWarning("MetaX provider manifest is unavailable: {Message}", e.Message);
                return null;
            }
        }
        finally
        {
            _manifestLock.Release();
        }
    }

    private async Task<ProviderManifest> FetchManifestAsync(CancellationToken cancellationToken)
    {
        var apiUrl = ComposeUrl(StatusApi, new NameValueCollection());
        var status = await GetDataAsync<MetaXStatusInfo>(apiUrl, true, null, null, cancellationToken,
            includeCookies: false).ConfigureAwait(false);

        var routes = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (category, providers) in status.RouteProviders ??
                 new Dictionary<string, IReadOnlyList<string>>())
        {
            routes[category] = providers ?? Array.Empty<string>();
        }

        return new ProviderManifest
        {
            RouteProviders = routes,
            CookieTransportVersion = status.CookieTransportVersion ?? string.Empty
        };
    }

    private async Task<T> GetDataAsync<T>(string url, bool requireAuth, string category, string explicitProvider,
        CancellationToken cancellationToken, bool includeCookies = true, bool allowUnscopedCookies = false)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("User-Agent", DefaultUserAgent);

        if (requireAuth && !string.IsNullOrWhiteSpace(Configuration.Token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Configuration.Token);

        if (includeCookies)
            await AddProviderCookiesAsync(request, category, explicitProvider, allowUnscopedCookies,
                cancellationToken).ConfigureAwait(false);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.Content == null)
            throw new Exception($"API request failed with HTTP {(int)response.StatusCode} and no response body.");

        var apiResponse = await response.Content
            .ReadFromJsonAsync<ResponseInfo<T>>(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (apiResponse == null)
            throw new Exception("Response envelope is null.");

        if (!response.IsSuccessStatusCode)
        {
            if (apiResponse.Error != null)
                throw new Exception($"API request error: {apiResponse.Error.Code} ({apiResponse.Error.Message})");

            throw new Exception($"API request failed with HTTP {(int)response.StatusCode}.");
        }

        if (apiResponse.Data == null)
            throw new Exception("Response data field is null");

        return apiResponse.Data;
    }

    private async Task AddProviderCookiesAsync(HttpRequestMessage request, string category, string explicitProvider,
        bool allowUnscopedCookies, CancellationToken cancellationToken)
    {
        var result = await BuildProviderCookieHeaderAsync(category, explicitProvider, allowUnscopedCookies,
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var warning in result.Warnings)
            _logger.LogWarning("{Warning}", warning);

        if (string.IsNullOrEmpty(result.HeaderValue))
            return;

        request.Headers.TryAddWithoutValidation(ProviderCookiesHeaderName, result.HeaderValue);
    }

    internal async Task<ProviderCookieHeaderResult> BuildProviderCookieHeaderAsync(string category,
        string explicitProvider, CancellationToken cancellationToken)
    {
        return await BuildProviderCookieHeaderAsync(category, explicitProvider, allowUnscopedCookies: false,
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task<ProviderCookieHeaderResult> BuildProviderCookieHeaderAsync(string category,
        string explicitProvider, bool allowUnscopedCookies, CancellationToken cancellationToken)
    {
        var cookies = Configuration.GetProviderCookieDictionary();
        if (cookies.Count == 0)
            return ProviderCookieHeaderResult.Empty;

        if (string.IsNullOrWhiteSpace(category) && string.IsNullOrWhiteSpace(explicitProvider) &&
            !allowUnscopedCookies)
            return new ProviderCookieHeaderResult(null, new[]
            {
                "No Jellyfin library category is mapped for this request; provider cookies were omitted."
            });

        ProviderManifest manifest = null;
        if (!string.IsNullOrWhiteSpace(category) && string.IsNullOrWhiteSpace(explicitProvider))
            manifest = await GetManifestAsync(cancellationToken).ConfigureAwait(false);

        return ProviderCookieHeaderBuilder.Build(cookies, category, manifest?.RouteProviders, explicitProvider);
    }

    private bool IsMetaXServerUrl(string url)
    {
        var server = Configuration.Server?.Trim();
        if (string.IsNullOrWhiteSpace(server) ||
            !Uri.TryCreate(server, UriKind.Absolute, out var serverUri) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var requestUri))
            return false;

        if (!string.Equals(requestUri.Scheme, serverUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(requestUri.Host, serverUri.Host, StringComparison.OrdinalIgnoreCase) ||
            requestUri.Port != serverUri.Port)
            return false;

        var basePath = serverUri.AbsolutePath.TrimEnd('/');
        var requestPath = requestUri.AbsolutePath;
        return string.IsNullOrEmpty(basePath) ||
               requestPath.Equals(basePath, StringComparison.OrdinalIgnoreCase) ||
               requestPath.StartsWith(basePath + "/", StringComparison.OrdinalIgnoreCase);
    }

    private string ComposeUrl(string path, NameValueCollection nv)
    {
        var server = Configuration.Server?.Trim();
        if (string.IsNullOrWhiteSpace(server))
            throw new InvalidOperationException("MetaX server URL is not configured.");

        var baseUri = new Uri(server.TrimEnd('/') + "/", UriKind.Absolute);
        var uriBuilder = new UriBuilder(new Uri(baseUri, path.TrimStart('/')));
        var query = HttpUtility.ParseQueryString(string.Empty);
        foreach (string key in nv)
        {
            var value = nv.Get(key);
            if (!string.IsNullOrEmpty(value))
                query.Add(key, value);
        }

        uriBuilder.Query = query.ToString() ?? string.Empty;
        return uriBuilder.Uri.ToString();
    }

    private string ComposeImageApiUrl(string path, string provider, string id, string url = default,
        double ratio = -1, double position = -1, bool auto = false, string badge = default,
        string category = null)
    {
        return ComposeUrl(JoinPath(path, provider, id), new NameValueCollection
        {
            { "url", url },
            { "ratio", ratio.ToString("R") },
            { "pos", position.ToString("R") },
            { "auto", auto.ToString() },
            { "badge", badge },
            { "quality", Configuration.DefaultImageQuality.ToString() },
            { "category", category }
        });
    }

    private string ComposeInfoApiUrl(string path, string provider, string id, bool lazy, string category = null)
    {
        return ComposeUrl(JoinPath(path, provider, id), new NameValueCollection
        {
            { "lazy", lazy.ToString() },
            { "category", category }
        });
    }

    private static string JoinPath(string path, params string[] segments)
    {
        var builder = new StringBuilder(path.TrimEnd('/'));
        foreach (var segment in segments)
            builder.Append('/').Append(Uri.EscapeDataString(segment ?? string.Empty));
        return builder.ToString();
    }

    private string ComposeSearchApiUrl(string path, string q, string provider, bool fallback,
        string category = null)
    {
        return ComposeUrl(path, new NameValueCollection
        {
            { "q", q },
            { "provider", provider },
            { "fallback", fallback.ToString() },
            { "category", category }
        });
    }

    private string ComposeTranslateApiUrl(string path, string q, string from, string to, string engine,
        NameValueCollection nv = null)
    {
        return ComposeUrl(path, new NameValueCollection
        {
            { "q", q },
            { "from", from },
            { "to", to },
            { "engine", engine },
            nv ?? new NameValueCollection()
        });
    }
}

public sealed class ProviderCookieHeaderResult
{
    public static readonly ProviderCookieHeaderResult Empty = new(null, Array.Empty<string>());

    public ProviderCookieHeaderResult(string headerValue, IReadOnlyList<string> warnings)
    {
        HeaderValue = headerValue;
        Warnings = warnings ?? Array.Empty<string>();
    }

    public string HeaderValue { get; }

    public IReadOnlyList<string> Warnings { get; }
}

public sealed class ProviderCookieConfigurationException : Exception
{
    public ProviderCookieConfigurationException(string message) : base(message)
    {
    }
}
