#if !__EMBY__
using Jellyfin.Plugin.MetaX.ExternalIds;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.MetaX.Providers;

public class ExternalUrlProvider : IExternalUrlProvider
{
    public string Name => Plugin.ProviderName;

    public IEnumerable<string> GetExternalUrls(BaseItem item)
    {
        if (item.TryGetProviderId(Plugin.ProviderId, out var pid))
        {
            switch (item)
            {
                case Movie:
                    yield return string.Format(new MovieExternalId().UrlFormatString, pid);
                    break;
                case Person:
                    yield return string.Format(new ActorExternalId().UrlFormatString, pid);
                    break;
            }
        }
    }
}
#endif