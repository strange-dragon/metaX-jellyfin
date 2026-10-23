using System.Xml.Serialization;
using Jellyfin.Plugin.MetaX.Configuration;

namespace Jellyfin.Plugin.MetaX.Tests;

public class PluginConfigurationTests
{
    [Fact]
    public void GetProviderCookieDictionary_TrimsAndUsesLastValue()
    {
        var config = new PluginConfiguration
        {
            ProviderCookies = new List<ProviderCookieEntry>
            {
                new() { Provider = " JavDB ", Cookie = "first=1" },
                new() { Provider = "javdb", Cookie = "second=2" },
                new() { Provider = "", Cookie = "ignored=1" },
                new() { Provider = "FANZA", Cookie = " " }
            }
        };

        var cookies = config.GetProviderCookieDictionary();

        Assert.Single(cookies);
        Assert.Equal("second=2", cookies["JAVDB"]);
    }

    [Fact]
    public void GetLibraryCategoryDictionary_NormalizesAliasesAndTrimsIds()
    {
        var config = new PluginConfiguration
        {
            LibraryCategories = new List<LibraryCategoryEntry>
            {
                new() { LibraryId = " library-a ", Category = "FC2" },
                new() { LibraryId = "library-b", Category = " Animation " },
                new() { LibraryId = "", Category = "western" }
            }
        };

        var categories = config.GetLibraryCategoryDictionary();

        Assert.Equal(2, categories.Count);
        Assert.Equal("uncensored", categories["library-a"]);
        Assert.Equal("hentai", categories["library-b"]);
    }

    [Fact]
    public void XmlSerializer_IgnoresRemovedLegacySettings()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <PluginConfiguration xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
              <Server>https://metax.example.com</Server>
              <Token>legacy-token</Token>
              <EnableMovieProviderFilter>true</EnableMovieProviderFilter>
              <ProviderCookies>
                <ProviderCookieEntry>
                  <Provider>JavDB</Provider>
                  <Cookie>session=abc</Cookie>
                </ProviderCookieEntry>
              </ProviderCookies>
            </PluginConfiguration>
            """;

        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        using var reader = new StringReader(xml);
        var config = Assert.IsType<PluginConfiguration>(serializer.Deserialize(reader));

        Assert.Equal("https://metax.example.com", config.Server);
        Assert.Equal("session=abc", config.GetProviderCookieDictionary()["JavDB"]);
    }

    [Fact]
    public void XmlSerializer_RoundTripsCookieAndCategoryEntries()
    {
        var config = new PluginConfiguration
        {
            Server = "https://metax.example.com",
            ProviderCookies = new List<ProviderCookieEntry>
            {
                new() { Provider = "JavDB", Cookie = "session=abc" }
            },
            LibraryCategories = new List<LibraryCategoryEntry>
            {
                new() { LibraryId = "library-a", Category = "uncensored" }
            }
        };

        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        using var writer = new StringWriter();
        serializer.Serialize(writer, config);
        var xml = writer.ToString();

        Assert.Contains("ProviderCookies", xml);
        Assert.Contains("LibraryCategories", xml);

        using var reader = new StringReader(xml);
        var roundTripped = Assert.IsType<PluginConfiguration>(serializer.Deserialize(reader));
        Assert.Equal("session=abc", roundTripped.GetProviderCookieDictionary()["JavDB"]);
        Assert.Equal("uncensored", roundTripped.GetLibraryCategoryDictionary()["library-a"]);
    }
}
