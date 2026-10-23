using Jellyfin.Plugin.MetaX.Helpers;
using Jellyfin.Plugin.MetaX.Metadata;

namespace Jellyfin.Plugin.MetaX.Tests;

public class SearchResultHelperTests
{
    [Fact]
    public void NormalizeCatalogNumber_HandlesCaseWidthAndSeparators()
    {
        Assert.Equal("SSIS001", SearchResultHelper.NormalizeCatalogNumber(" ｓｓｉｓ-001 "));
        Assert.Equal("FC2123456", SearchResultHelper.NormalizeCatalogNumber("FC2-PPV-123456"));
        Assert.True(SearchResultHelper.LooksLikeCatalogNumber("390JAC-079"));
        Assert.True(SearchResultHelper.LooksLikeCatalogNumber("SSIS001"));
        Assert.False(SearchResultHelper.LooksLikeCatalogNumber("Yua Mikami"));
    }

    [Fact]
    public void FilterAndDeduplicate_KeepsOnlyExactCatalogMatches()
    {
        var results = new[]
        {
            Result("MetaX", "SSIS-001", "Exact title"),
            Result("JavDB", "SSIS-001", "Duplicate exact title"),
            Result("JavDB", "SSIS-002", "Unrelated series result"),
            Result("JavDB", "SSIS-003", "Unrelated series result")
        };

        var filtered = SearchResultHelper.FilterAndDeduplicate(results, "ssis-001");

        var result = Assert.Single(filtered);
        Assert.Equal("MetaX", result.Provider);
        Assert.Equal("SSIS-001", result.Number);
    }

    [Fact]
    public void FilterAndDeduplicate_DoesNotFilterNonCatalogQueries()
    {
        var results = new[]
        {
            Result("MetaX", "ABP-001", "First"),
            Result("JavDB", "ABP-002", "Second")
        };

        var filtered = SearchResultHelper.FilterAndDeduplicate(results, "Yua Mikami");

        Assert.Equal(2, filtered.Count);
    }

    [Fact]
    public void BuildDisplayName_NeverReturnsEmptyOrDuplicateNumber()
    {
        Assert.Equal("SSIS-001 Title", SearchResultHelper.BuildDisplayName(Result("MetaX", "SSIS-001", "Title")));
        Assert.Equal("SSIS-001", SearchResultHelper.BuildDisplayName(Result("MetaX", "SSIS-001", "SSIS-001")));
        Assert.Equal("ABC-123", SearchResultHelper.BuildDisplayName(Result("JavDB", null, null, "ABC-123")));
    }

    private static MovieSearchResult Result(string provider, string number, string title, string id = null)
    {
        return new MovieSearchResult
        {
            Provider = provider,
            Id = id ?? number,
            Number = number,
            Title = title,
            ThumbUrl = "http://metax.test/image.jpg"
        };
    }
}
