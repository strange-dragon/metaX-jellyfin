using Jellyfin.Plugin.MetaX.Extensions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
#if __EMBY__
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Logging;

#else
using Microsoft.Extensions.Logging;
#endif

namespace Jellyfin.Plugin.MetaX.Providers;

public class MovieImageProvider : BaseProvider, IRemoteImageProvider, IHasOrder
{
#if __EMBY__
    public MovieImageProvider(ILogManager logManager, MetaXApiClient api, LibraryCategoryResolver categories)
        : base(logManager.CreateLogger<MovieImageProvider>(), api)
#else
    public MovieImageProvider(ILogger<MovieImageProvider> logger, MetaXApiClient api,
        LibraryCategoryResolver categories) : base(logger, api)
#endif
    {
        _categories = categories;
    }

    private readonly LibraryCategoryResolver _categories;

#if __EMBY__
    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, LibraryOptions libraryOptions,
        CancellationToken cancellationToken)
#else
    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
#endif
    {
        var pid = item.GetPid(Plugin.ProviderId);
        if (string.IsNullOrWhiteSpace(pid.Id) || string.IsNullOrWhiteSpace(pid.Provider))
            return Enumerable.Empty<RemoteImageInfo>();

        var category = _categories.Resolve(item);
        var m = await Api.GetMovieInfoAsync(pid.Provider, pid.Id, category, cancellationToken);
        if (m == null)
            return Enumerable.Empty<RemoteImageInfo>();

        var images = new List<RemoteImageInfo>
        {
            new()
            {
                ProviderName = Name,
                Type = ImageType.Primary,
                Url = Api.GetPrimaryImageApiUrl(m.Provider, m.Id, pid.Position ?? -1, category: category)
            },
            new()
            {
                ProviderName = Name,
                Type = ImageType.Thumb,
                Url = Api.GetThumbImageApiUrl(m.Provider, m.Id, category: category)
            },
            new()
            {
                ProviderName = Name,
                Type = ImageType.Backdrop,
                Url = Api.GetBackdropImageApiUrl(m.Provider, m.Id, category: category)
            }
        };

        foreach (var imageUrl in m.PreviewImages ?? Enumerable.Empty<string>())
        {
            images.Add(new RemoteImageInfo
            {
                ProviderName = Name,
                Type = ImageType.Primary,
                Url = Api.GetPrimaryImageApiUrl(m.Provider, m.Id, imageUrl, pid.Position ?? -1,
                    category: category)
            });

            images.Add(new RemoteImageInfo
            {
                ProviderName = Name,
                Type = ImageType.Thumb,
                Url = Api.GetThumbImageApiUrl(m.Provider, m.Id, imageUrl, category: category)
            });

            images.Add(new RemoteImageInfo
            {
                ProviderName = Name,
                Type = ImageType.Backdrop,
                Url = Api.GetBackdropImageApiUrl(m.Provider, m.Id, imageUrl, category: category)
            });
        }

        return images;
    }

    public bool Supports(BaseItem item)
    {
        // Shows libraries expose Series items. Supporting them here lets the
        // same MetaX movie record provide series artwork as well.
        return item is Movie or Series;
    }

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        return new List<ImageType>
        {
            ImageType.Primary,
            ImageType.Thumb,
            ImageType.Backdrop
        };
    }
}