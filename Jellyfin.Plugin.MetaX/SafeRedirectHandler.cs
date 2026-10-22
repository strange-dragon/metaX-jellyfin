using System.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MetaX;

/// <summary>
/// Follows redirects manually so provider credentials are never forwarded to a
/// host outside the configured MetaX origin. SocketsHttpHandler automatic
/// redirects are disabled when this handler is installed.
/// </summary>
internal sealed class SafeRedirectHandler : DelegatingHandler
{
    private const int MaxRedirects = 5;

    private readonly Func<string, bool> _isMetaXUrl;
    private readonly ILogger _logger;

    public SafeRedirectHandler(HttpMessageHandler innerHandler, Func<string, bool> isMetaXUrl, ILogger logger)
        : base(innerHandler)
    {
        _isMetaXUrl = isMetaXUrl ?? throw new ArgumentNullException(nameof(isMetaXUrl));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var redirectCount = 0;

        while (IsRedirect(response.StatusCode))
        {
            if (redirectCount++ >= MaxRedirects)
            {
                response.Dispose();
                throw new HttpRequestException("Too many redirects.");
            }

            var location = response.Headers.Location;
            if (location == null)
                return response;

            if (!Uri.TryCreate(request.RequestUri, location, out var target) ||
                (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps))
            {
                response.Dispose();
                throw new HttpRequestException("Redirect target is not a valid HTTP URL.");
            }

            var nextRequest = CloneRequest(request, target);
            if (!_isMetaXUrl(target.ToString()))
            {
                // A redirect can target an external CDN or an attacker-controlled
                // host. Never carry provider cookies or the metaX bearer token
                // across that boundary.
                nextRequest.Headers.Remove(MetaXApiClient.ProviderCookiesHeaderName);
                nextRequest.Headers.Authorization = null;
                _logger.LogDebug("Redirect left the configured MetaX origin; credentials were removed.");
            }

            response.Dispose();
            request = nextRequest;
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage source, Uri target)
    {
        var clone = new HttpRequestMessage(source.Method, target);
        foreach (var header in source.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        return clone;
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.Moved
            or HttpStatusCode.Redirect
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;
    }
}
