using Jellyfin.Plugin.MetaX.Metadata;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MetaX;

[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("MetaX")]
[Route("Plugins/MetaX")]
public sealed class MetaXController : ControllerBase
{
    private readonly MetaXApiClient _api;

    public MetaXController(MetaXApiClient api)
    {
        _api = api;
    }

    [HttpGet("Providers")]
    public async Task<ActionResult<ProviderCatalog>> GetProviders(CancellationToken cancellationToken)
    {
        return Ok(await _api.GetProviderCatalogAsync(cancellationToken).ConfigureAwait(false));
    }
}
