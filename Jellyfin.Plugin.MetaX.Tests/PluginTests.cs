using Jellyfin.Plugin.MetaX;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.MetaX.Tests;

public class PluginTests
{
    [Fact]
    public void IdentityAndConfigurationPageAreStable()
    {
        var plugin = new Plugin(new FakeApplicationPaths(), null!);

        Assert.Equal(Guid.Parse("5597841f-fe55-4d59-991e-c3bdbf7d8d1d"), plugin.Id);
        Assert.Equal("MetaX", plugin.Name);

        var page = Assert.Single(plugin.GetPages());
        Assert.Equal("MetaX", page.Name);
        Assert.Equal("Jellyfin.Plugin.MetaX.Configuration.configPage.html", page.EmbeddedResourcePath);
        Assert.NotNull(typeof(Plugin).Assembly.GetManifestResourceStream(page.EmbeddedResourcePath));
    }

    private sealed class FakeApplicationPaths : IApplicationPaths
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "metax-plugin-tests");

        public string ProgramDataPath => _root;
        public string WebPath => _root;
        public string ProgramSystemPath => _root;
        public string DataPath => _root;
        public string ImageCachePath => _root;
        public string PluginsPath => Path.Combine(_root, "plugins");
        public string PluginConfigurationsPath => Path.Combine(_root, "config");
        public string LogDirectoryPath => Path.Combine(_root, "logs");
        public string ConfigurationDirectoryPath => Path.Combine(_root, "config");
        public string SystemConfigurationFilePath => Path.Combine(_root, "config", "system.xml");
        public string CachePath => Path.Combine(_root, "cache");
        public string TempDirectory => Path.Combine(_root, "temp");
        public string VirtualDataPath => "%AppDataPath%";
        public string TrickplayPath => Path.Combine(_root, "trickplay");
        public string BackupPath => Path.Combine(_root, "backup");

        public void MakeSanityCheckOrThrow()
        {
        }

        public void CreateAndCheckMarker(string path, string markerName, bool recursive = false)
        {
        }
    }
}
