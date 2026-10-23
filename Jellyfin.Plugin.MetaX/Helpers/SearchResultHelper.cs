using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.MetaX.Metadata;

namespace Jellyfin.Plugin.MetaX.Helpers;

/// <summary>
/// Normalizes and de-duplicates backend search results. Some providers (most
/// notably JavDB) answer a catalog-number query with an entire series of
/// unrelated entries. Jellyfin should only receive the exact title the user
/// asked for in that case.
/// </summary>
internal static class SearchResultHelper
{
    private static readonly Regex CatalogNumberPattern = new(
        @"^(?:[0-9]{0,4}[a-z]{1,16}(?:[._-]?[a-z0-9]+)*[._-][0-9]{2,}(?:[._-][a-z0-9]+)*|[a-z]{1,16}[-_ ]?[0-9]{2,}[a-z0-9._-]*|[0-9]{3,}(?:_[0-9]+)+|fc2[-_ ]?(?:ppv[-_ ]?)?[0-9]+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private const string AggregatedProviderName = "MetaX";

    public static string NormalizeCatalogNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        normalized = Regex.Replace(normalized, @"FC2[-_ ]?PPV", "FC2", RegexOptions.IgnoreCase);

        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (char.IsLetterOrDigit(character))
                builder.Append(character);
        }

        return builder.ToString();
    }

    public static bool LooksLikeCatalogNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Normalize(NormalizationForm.FormKC).Trim();
        return CatalogNumberPattern.IsMatch(normalized);
    }

    public static List<MovieSearchResult> FilterAndDeduplicate(IEnumerable<MovieSearchResult> results,
        string query)
    {
        var candidates = (results ?? Enumerable.Empty<MovieSearchResult>())
            .Where(result => result != null)
            .Where(result => !string.IsNullOrWhiteSpace(result.Number)
                             || !string.IsNullOrWhiteSpace(result.Title)
                             || !string.IsNullOrWhiteSpace(result.Id))
            .ToList();

        if (candidates.Count == 0)
            return candidates;

        var exactQuery = LooksLikeCatalogNumber(query);
        var queryKey = NormalizeCatalogNumber(query);
        if (exactQuery && queryKey.Length > 0)
        {
            var exactMatches = candidates
                .Where(result => NumberMatches(result, queryKey))
                .ToList();

            // A catalog-number search must never fall back to the provider's
            // whole series. If no exact row exists, show no result instead of
            // flooding Jellyfin with unrelated titles.
            if (exactMatches.Count == 0)
                return new List<MovieSearchResult>();

            candidates = exactMatches;
        }

        // The backend prepends one merged MetaX candidate, then repeats the
        // provider entries it was built from. Keep one row per provider for
        // the same catalog number so users can still choose JavDB, JavBus,
        // FANZA, and other providers instead of losing every alternative.
        var unique = new Dictionary<string, MovieSearchResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var provider = (candidate.Provider ?? string.Empty).Trim();
            var number = NormalizeCatalogNumber(candidate.Number);
            var key = number.Length == 0
                ? $"{provider}\u0000{candidate.Id ?? string.Empty}"
                : $"{provider}\u0000{number}";

            if (!unique.TryGetValue(key, out var current) || IsBetter(candidate, current))
                unique[key] = candidate;
        }

        var limit = exactQuery ? 10 : 20;
        return unique.Values
            .OrderByDescending(IsAggregated)
            .ThenByDescending(HasTitle)
            .ThenByDescending(HasImage)
            .Take(limit)
            .ToList();
    }

    public static string BuildDisplayName(MovieSearchResult result)
    {
        var number = result?.Number?.Trim();
        var title = result?.Title?.Trim();

        if (!string.IsNullOrWhiteSpace(title) &&
            !string.Equals(title, number, StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(number) ? title : $"{number} {title}";

        if (!string.IsNullOrWhiteSpace(number))
            return number;

        if (!string.IsNullOrWhiteSpace(result?.Id))
            return result.Id.Trim();

        return string.IsNullOrWhiteSpace(result?.Provider) ? AggregatedProviderName : result.Provider.Trim();
    }

    private static bool NumberMatches(MovieSearchResult result, string queryKey)
    {
        return string.Equals(NormalizeCatalogNumber(result.Number), queryKey, StringComparison.OrdinalIgnoreCase)
               || string.Equals(NormalizeCatalogNumber(result.Id), queryKey, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBetter(MovieSearchResult candidate, MovieSearchResult current)
    {
        var candidateScore = Score(candidate);
        var currentScore = Score(current);
        return candidateScore > currentScore;
    }

    private static int Score(MovieSearchResult result)
    {
        var score = 0;
        if (IsAggregated(result))
            score += 4;
        if (HasTitle(result))
            score += 2;
        if (HasImage(result))
            score += 1;
        return score;
    }

    private static bool IsAggregated(MovieSearchResult result)
    {
        return string.Equals(result?.Provider, AggregatedProviderName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasTitle(MovieSearchResult result)
    {
        var title = result?.Title?.Trim();
        return !string.IsNullOrWhiteSpace(title)
               && !string.Equals(title, result?.Number?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasImage(MovieSearchResult result)
    {
        return !string.IsNullOrWhiteSpace(result?.ThumbUrl)
               || !string.IsNullOrWhiteSpace(result?.CoverUrl);
    }
}
