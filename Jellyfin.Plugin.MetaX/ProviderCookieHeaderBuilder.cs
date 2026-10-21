using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.MetaX.Configuration;

namespace Jellyfin.Plugin.MetaX;

/// <summary>
/// Builds the request-scoped X-MetaX-Provider-Cookies header. The builder is
/// pure so cookie scope and secret-safe error behavior can be tested without a
/// running Jellyfin server.
/// </summary>
public static class ProviderCookieHeaderBuilder
{
    public const int MaxHeaderBytes = 16 * 1024;

    private static readonly Dictionary<string, string[]> ProviderAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["DMM"] = new[] { "FANZA" },
            ["MGStage"] = new[] { "MGS" },
            ["ThePornDB"] = new[] { "ThePornDBMovie", "ThePornDBScene" }
        };

    public static ProviderCookieHeaderResult Build(
        IReadOnlyDictionary<string, string> cookies,
        string category,
        IReadOnlyDictionary<string, IReadOnlyList<string>> routeProviders,
        string explicitProvider)
    {
        var warnings = new List<string>();
        if (cookies == null || cookies.Count == 0)
            return new ProviderCookieHeaderResult(null, warnings);

        var normalizedCategory = PluginConfiguration.NormalizeCategory(category);
        HashSet<string> allowedProviders = null;

        if (!string.IsNullOrWhiteSpace(explicitProvider))
        {
            allowedProviders = new HashSet<string>(ExpandAliases(explicitProvider), StringComparer.OrdinalIgnoreCase);
        }
        else if (!string.IsNullOrWhiteSpace(normalizedCategory))
        {
            IReadOnlyList<string> providers = null;
            if (routeProviders != null)
            {
                if (!routeProviders.TryGetValue(normalizedCategory, out providers))
                {
                    foreach (var entry in routeProviders)
                    {
                        if (!string.Equals(entry.Key, normalizedCategory, StringComparison.OrdinalIgnoreCase))
                            continue;
                        providers = entry.Value;
                        break;
                    }
                }
            }

            if (providers == null)
            {
                warnings.Add(
                    $"MetaX provider manifest is unavailable for category '{normalizedCategory}'; provider cookies were omitted.");
                return new ProviderCookieHeaderResult(null, warnings);
            }

            allowedProviders = new HashSet<string>(providers, StringComparer.OrdinalIgnoreCase);
        }

        var selected = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (configuredProvider, cookieValue) in cookies)
        {
            var provider = configuredProvider?.Trim();
            var value = cookieValue?.Trim();
            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(value))
                continue;

            var aliases = ExpandAliases(provider);
            if (allowedProviders != null)
            {
                var matched = aliases.Where(allowedProviders.Contains).ToArray();
                if (matched.Length == 0)
                    continue;

                if (value.Contains('\r') || value.Contains('\n'))
                    throw new ProviderCookieConfigurationException(
                        $"Provider cookie for '{provider}' contains a newline and was rejected.");

                // Collapse a single-target alias to the canonical manifest name.
                // A multi-target alias stays in its original form so the server
                // can expand it for the providers that are actually available.
                selected[matched.Length == 1 ? matched[0] : provider] = value;
            }
            else
            {
                if (value.Contains('\r') || value.Contains('\n'))
                    throw new ProviderCookieConfigurationException(
                        $"Provider cookie for '{provider}' contains a newline and was rejected.");

                selected[provider] = value;
            }
        }

        if (selected.Count == 0)
            return new ProviderCookieHeaderResult(null, warnings);

        var json = JsonSerializer.Serialize(selected);
        var byteCount = Encoding.UTF8.GetByteCount(json);
        if (byteCount > MaxHeaderBytes)
            throw new ProviderCookieConfigurationException(
                $"Provider cookie header is {byteCount} bytes, over the {MaxHeaderBytes}-byte limit.");

        return new ProviderCookieHeaderResult(json, warnings);
    }

    public static IReadOnlyList<string> ExpandAliases(string provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
            return Array.Empty<string>();

        var trimmed = provider.Trim();
        return ProviderAliases.TryGetValue(trimmed, out var aliases)
            ? aliases
            : new[] { trimmed };
    }
}
