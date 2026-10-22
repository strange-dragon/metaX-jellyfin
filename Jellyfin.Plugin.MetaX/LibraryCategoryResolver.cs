using Jellyfin.Plugin.MetaX.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MetaX;

/// <summary>
/// Resolves the configured metaX category for a Jellyfin library item.
/// </summary>
public sealed class LibraryCategoryResolver
{
    private readonly ILibraryManager _libraryManager;
    private readonly Func<PluginConfiguration> _configuration;
    private readonly ILogger<LibraryCategoryResolver> _logger;

    public LibraryCategoryResolver(
        ILibraryManager libraryManager,
        Func<PluginConfiguration> configuration,
        ILogger<LibraryCategoryResolver> logger)
    {
        _libraryManager = libraryManager;
        _configuration = configuration;
        _logger = logger;
    }

    public string Resolve(BaseItem item)
    {
        if (item == null)
            return null;

        try
        {
            var mappings = _configuration().GetLibraryCategoryDictionary();
            if (mappings.Count == 0)
                return null;

            foreach (var folder in _libraryManager.GetCollectionFolders(item) ?? Enumerable.Empty<Folder>())
            {
                if (TryGetCategory(mappings, folder.Id, out var category))
                    return category;
            }
        }
        catch (Exception e)
        {
            _logger.LogDebug("Unable to resolve Jellyfin library category: {Message}", e.Message);
        }

        return null;
    }

    public string Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            var item = _libraryManager.FindByPath(path, false)
                       ?? _libraryManager.FindByPath(path, null);
            return item == null ? null : Resolve(item);
        }
        catch (Exception e)
        {
            _logger.LogDebug("Unable to resolve the Jellyfin item's library category: {Message}", e.Message);
            return null;
        }
    }

    private static bool TryGetCategory(IReadOnlyDictionary<string, string> mappings, Guid folderId, out string category)
    {
        return mappings.TryGetValue(folderId.ToString("N"), out category)
               || mappings.TryGetValue(folderId.ToString("D"), out category);
    }
}
