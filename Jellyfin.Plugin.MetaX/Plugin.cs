using Jellyfin.Plugin.MetaX.Configuration;
using MediaBrowser.Common.Plugins;
#if __EMBY__
using MediaBrowser.Common;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Drawing;

#else
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Common.Configuration;
#endif

namespace Jellyfin.Plugin.MetaX;

#if __EMBY__
public class Plugin : BasePluginSimpleUI<PluginConfiguration>, IHasThumbImage
{
    public Plugin(IApplicationHost applicationHost) : base(applicationHost)
    {
        Instance = this;
    }
#else
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths,
        xmlSerializer)
    {
        Instance = this;
    }
#endif

    public const string ProviderName = "MetaX";

    public const string ProviderId = "MetaX";

    public override string Name => ProviderName;

    public override string Description => "MetaX Plugin for Jellyfin";

    public override Guid Id => Guid.Parse("5597841f-fe55-4d59-991e-c3bdbf7d8d1d");

    public static Plugin Instance { get; private set; }

#if !__EMBY__
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
            }
        };
    }
#endif

#if __EMBY__
    public PluginConfiguration Configuration => GetOptions();

    public Stream GetThumbImage()
    {
        return GetType().Assembly.GetManifestResourceStream($"{GetType().Namespace}.thumb.png");
    }

    public ImageFormat ThumbImageFormat => ImageFormat.Png;
#endif
}