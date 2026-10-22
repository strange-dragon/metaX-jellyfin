#if __EMBY__
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Logging;
using MediaBrowser.Controller.Providers;
#else
using System.Web;
using Microsoft.Extensions.Logging;
#endif

namespace Jellyfin.Plugin.MetaX.Providers;

#if __EMBY__
public abstract class BaseProvider : IHasSupportedExternalIdentifiers
#else
public abstract class BaseProvider
#endif
{
    protected readonly ILogger Logger;
    protected readonly MetaXApiClient Api;

    protected BaseProvider(ILogger logger, MetaXApiClient api)
    {
        Logger = logger;
        Api = api;
    }

    protected static Configuration.PluginConfiguration Configuration => Plugin.Instance.Configuration;

    public virtual int Order => 1;

    public virtual string Name => Plugin.ProviderName;

#if __EMBY__
    public string[] GetSupportedExternalIdentifiers()
    {
        return new[] { Plugin.ProviderName };
    }

    public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("The MetaX plugin targets Jellyfin only.");
    }
#else
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        Logger.LogDebug("GetImageResponse for url: {Url}", url);
        var category = GetCategoryFromImageUrl(url);
        return Api.GetImageResponseAsync(url, category, cancellationToken);
    }

    private static string GetCategoryFromImageUrl(string url)
    {
        try
        {
            var query = HttpUtility.ParseQueryString(new Uri(url).Query);
            return query["category"];
        }
        catch
        {
            return null;
        }
    }
#endif
}
