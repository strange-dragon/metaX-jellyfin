using Jellyfin.Plugin.MetaX.Extensions;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MetaX.Providers;

/// <summary>
/// Resolves a Person image through the backend actor search. Gfriends is
/// preferred because it usually has the cleanest portrait, but any actor
/// provider with an image is accepted as a fallback.
/// </summary>
internal static class PersonImageResolver
{
    private const string Gfriends = "Gfriends";

    public static async Task SetImageAsync(PersonInfo person, MetaXApiClient api, ImageProxyService imageProxy,
        ILogger logger, CancellationToken cancellationToken)
    {
        if (person == null || string.IsNullOrWhiteSpace(person.Name))
            return;

        try
        {
            var results = await api.SearchActorAsync(person.Name, cancellationToken).ConfigureAwait(false);
            var imageResult = results?
                .Where(result => result?.Images?.Any() == true)
                .OrderByDescending(result => string.Equals(result.Provider, Gfriends,
                    StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            if (imageResult == null)
            {
                logger.LogDebug("No MetaX actor image found for {Name}", person.Name);
                return;
            }

            var imageUrl = api.GetPrimaryImageApiUrl(imageResult.Provider, imageResult.Id,
                imageResult.Images.First(), 0.5, true);

            person.ImageUrl = GetProxiedImageUrl(imageUrl, imageProxy, logger);
            person.SetPid(Plugin.ProviderName, imageResult.Provider, imageResult.Id);
        }
        catch (Exception e)
        {
            logger.LogWarning("MetaX person image lookup failed for {Name}: {Message}", person.Name, e.Message);
        }
    }

    private static string GetProxiedImageUrl(string imageUrl, ImageProxyService imageProxy, ILogger logger)
    {
        if (imageProxy == null || string.IsNullOrWhiteSpace(imageUrl))
            return imageUrl;

        try
        {
            return imageProxy.CreateUrl(imageUrl, null);
        }
        catch (Exception e)
        {
            logger.LogWarning("Falling back to direct person image URL: {Message}", e.Message);
            return imageUrl;
        }
    }
}
