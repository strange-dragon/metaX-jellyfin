using Jellyfin.Plugin.MetaX.Extensions;
using Jellyfin.Plugin.MetaX.Helpers;
using Jellyfin.Plugin.MetaX.Metadata;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
#if __EMBY__
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Logging;
#else
using Jellyfin.Data.Enums;
using Microsoft.Extensions.Logging;
#endif

namespace Jellyfin.Plugin.MetaX.Providers;

/// <summary>
/// Movie metadata exposed for Jellyfin "Shows" libraries. A JAV title is a
/// single movie, but users often create a Shows library so Jellyfin treats the
/// title folder as a series. This provider maps that folder to the same MetaX
/// movie record and lets the normal image provider supply artwork.
/// </summary>
public class SeriesProvider : BaseProvider, IRemoteMetadataProvider<Series, SeriesInfo>, IHasOrder
{
    private const string Rating = "JP-18+";

    private readonly LibraryCategoryResolver _categories;
    private readonly ImageProxyService _imageProxy;

#if __EMBY__
    public SeriesProvider(ILogManager logManager, MetaXApiClient api, LibraryCategoryResolver categories)
        : base(logManager.CreateLogger<SeriesProvider>(), api)
    {
        _categories = categories;
        _imageProxy = null;
    }
#else
    public SeriesProvider(ILogger<SeriesProvider> logger, MetaXApiClient api, LibraryCategoryResolver categories,
        ImageProxyService imageProxy = null)
        : base(logger, api)
    {
        _categories = categories;
        _imageProxy = imageProxy;
    }
#endif

    public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken)
    {
        var pid = info.GetPid(Plugin.ProviderId);
        var category = _categories.Resolve(info.Path);

        if (string.IsNullOrWhiteSpace(pid.Id) || string.IsNullOrWhiteSpace(pid.Provider))
        {
            var firstResult = (await GetSearchResults(info, cancellationToken)).FirstOrDefault();
            if (firstResult != null)
                pid = firstResult.GetPid(Plugin.ProviderId);
        }

        if (string.IsNullOrWhiteSpace(pid.Id) || string.IsNullOrWhiteSpace(pid.Provider))
        {
            Logger.Warn("No matching series found for: {0}", info.Name);
            return new MetadataResult<Series>();
        }

        var m = await Api.GetMovieInfoAsync(pid.Provider, pid.Id, category, cancellationToken);
        if (m == null)
        {
            Logger.Warn("MetaX returned no movie for series: {0}", pid.ToString());
            return new MetadataResult<Series>();
        }

        var result = new MetadataResult<Series>
        {
            HasMetadata = true,
            Item = new Series
            {
                Name = BuildName(m),
                OriginalTitle = m.Title,
                Tagline = BuildTagline(m),
                Overview = m.Summary,
                OfficialRating = Rating,
                PremiereDate = m.ReleaseDate.GetValidDateTime(),
                ProductionYear = m.ReleaseDate.GetValidYear(),
                Genres = m.Genres?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray()
                         ?? Array.Empty<string>(),
                CommunityRating = m.Score > 0 ? (float)Math.Round(m.Score * 2, 1) : null
            }
        };

        result.Item.SetPid(Name, m.Provider, m.Id, pid.Position);

        if (!string.IsNullOrWhiteSpace(m.Maker))
            result.Item.AddStudio(m.Maker);
        if (!string.IsNullOrWhiteSpace(m.Series))
            result.Item.AddTag(m.Series);
        if (!string.IsNullOrWhiteSpace(m.Maker))
            result.Item.AddTag(m.Maker);
        if (!string.IsNullOrWhiteSpace(m.Label))
            result.Item.AddTag(m.Label);

        if (!string.IsNullOrWhiteSpace(m.Director))
            result.AddPerson(new PersonInfo
            {
                Name = m.Director,
#if __EMBY__
                Type = PersonType.Director
#else
                Type = PersonKind.Director
#endif
            });

        foreach (var actorName in m.Actors ?? Enumerable.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(actorName))
                continue;
            result.AddPerson(new PersonInfo
            {
                Name = actorName,
#if __EMBY__
                Type = PersonType.Actor
#else
                Type = PersonKind.Actor
#endif
            });
        }

        return result;
    }

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo info,
        CancellationToken cancellationToken)
    {
        var pid = info.GetPid(Plugin.ProviderId);
        var category = _categories.Resolve(info.Path);

        var searchResults = new List<MovieSearchResult>();
        if (string.IsNullOrWhiteSpace(pid.Id) || string.IsNullOrWhiteSpace(pid.Provider))
        {
            Logger.Info("Search for series: {0}", info.Name);
            searchResults.AddRange(await Api.SearchMovieAsync(info.Name, pid.Provider, category,
                cancellationToken));
        }
        else
        {
            Logger.Info("Search for series: {0}", pid.ToString());
            var m = await Api.GetMovieInfoAsync(pid.Provider, pid.Id, pid.Update != true, category,
                cancellationToken);
            if (m != null)
                searchResults.Add(m);
        }

        searchResults = SearchResultHelper.FilterAndDeduplicate(searchResults, info.Name);

        var results = new List<RemoteSearchResult>();
        foreach (var m in searchResults)
        {
            var displayName = SearchResultHelper.BuildDisplayName(m);
            var result = new RemoteSearchResult
            {
                Name = string.IsNullOrWhiteSpace(m.Provider) ? displayName : $"[{m.Provider}] {displayName}",
                SearchProviderName = Name,
                PremiereDate = m.ReleaseDate.GetValidDateTime(),
                ProductionYear = m.ReleaseDate.GetValidYear(),
                ImageUrl = GetSearchImageUrl(
                    Api.GetPrimaryImageApiUrl(m.Provider, m.Id, m.ThumbUrl, 1.0, false, category: category),
                    category)
            };
            result.SetPid(Name, m.Provider, m.Id, pid.Position);
            results.Add(result);
        }

        return results;
    }

    private string GetSearchImageUrl(string imageUrl, string category)
    {
        if (_imageProxy == null || string.IsNullOrWhiteSpace(imageUrl))
            return imageUrl;

        try
        {
            return _imageProxy.CreateDisplayUrl(imageUrl, category);
        }
        catch (Exception e)
        {
            Logger.Warn("Falling back to direct series image URL: {Message}", e.Message);
            return imageUrl;
        }
    }

    private static string BuildName(MovieSearchResult m)
    {
        var displayName = SearchResultHelper.BuildDisplayName(m);
        return string.IsNullOrWhiteSpace(displayName) ? m.Id : displayName;
    }

    private static string BuildTagline(MovieSearchResult m)
    {
        if (m.ReleaseDate == default)
            return null;

        return $"配信開始日 {m.ReleaseDate:yyyy-MM-dd}";
    }
}
