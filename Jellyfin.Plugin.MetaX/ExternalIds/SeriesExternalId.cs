using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
#if !__EMBY__
using MediaBrowser.Model.Providers;
#endif

namespace Jellyfin.Plugin.MetaX.ExternalIds;

public class SeriesExternalId : BaseExternalId
{
#if !__EMBY__
    public override ExternalIdMediaType? Type => ExternalIdMediaType.Series;
#endif

    public override bool Supports(IHasProviderIds item)
    {
        return item is Series;
    }
}
