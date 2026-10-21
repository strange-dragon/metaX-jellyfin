using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MetaX.Metadata;

public sealed class ProvidersInfo
{
    [JsonPropertyName("movie_providers")]
    public Dictionary<string, ProviderInfo> MovieProviders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("actor_providers")]
    public Dictionary<string, ProviderInfo> ActorProviders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class MetaXStatusInfo
{
    [JsonPropertyName("route_providers")]
    public Dictionary<string, IReadOnlyList<string>> RouteProviders { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("cookie_transport_version")]
    public string CookieTransportVersion { get; set; } = string.Empty;
}

public sealed class ProviderManifest
{
    public IReadOnlyDictionary<string, IReadOnlyList<string>> RouteProviders { get; set; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    public string CookieTransportVersion { get; set; } = string.Empty;
}

public sealed class ProviderCatalog
{
    public List<string> MovieProviders { get; set; } = new();

    public List<string> ActorProviders { get; set; } = new();

    public Dictionary<string, List<string>> RouteProviders { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public string CookieTransportVersion { get; set; } = string.Empty;

    public bool ManifestAvailable { get; set; }
}
