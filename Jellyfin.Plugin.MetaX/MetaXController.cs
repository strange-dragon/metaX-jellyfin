using Jellyfin.Plugin.MetaX.Metadata;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MetaX;

[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("MetaX")]
[Route("Plugins/MetaX")]
public sealed class MetaXController : ControllerBase
{
    private readonly MetaXApiClient _api;
    private readonly ImageProxyService _imageProxy;

    public MetaXController(MetaXApiClient api, ImageProxyService imageProxy)
    {
        _api = api;
        _imageProxy = imageProxy;
    }

    [HttpGet("Providers")]
    public async Task<ActionResult<ProviderCatalog>> GetProviders(CancellationToken cancellationToken)
    {
        return Ok(await _api.GetProviderCatalogAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Proxies an image requested by Jellyfin's Person image downloader. The
    /// endpoint is anonymous because that downloader has no user context, so
    /// the signed token is required and the target must remain on the
    /// configured MetaX origin.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("Images")]
    public async Task<ActionResult> GetImage([FromQuery] string token, CancellationToken cancellationToken)
    {
        if (!_imageProxy.TryReadToken(token, out var payload) || !_api.IsMetaXServerUrl(payload.Url))
            return Unauthorized();

        using var response = await _api.GetImageResponseAsync(payload.Url, payload.Category, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return StatusCode((int)response.StatusCode);

        if (response.Content == null)
            return StatusCode(StatusCodes.Status502BadGateway);

        Response.ContentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        Response.Headers.CacheControl = "private, max-age=300";
        await response.Content.CopyToAsync(Response.Body, cancellationToken).ConfigureAwait(false);
        return new EmptyResult();
    }
}
