using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.MetaX.Tests;

public class SafeRedirectHandlerTests
{
    [Fact]
    public async Task SendAsync_RemovesProviderCredentialsOnCrossOriginRedirect()
    {
        var requests = new List<HttpRequestMessage>();
        var inner = new StubHandler(request =>
        {
            requests.Add(request);
            return requests.Count == 1
                ? Redirect("https://cdn.example.net/image.jpg")
                : new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = CreateClient(inner);
        using var request = CreateAuthenticatedRequest("http://metax.test/v1/images/primary/JavDB/ABC-123");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, requests.Count);
        Assert.True(requests[0].Headers.Contains(MetaXApiClient.ProviderCookiesHeaderName));
        Assert.False(requests[1].Headers.Contains(MetaXApiClient.ProviderCookiesHeaderName));
        Assert.Null(requests[1].Headers.Authorization);
    }

    [Fact]
    public async Task SendAsync_KeepsProviderCredentialsOnSameOriginRedirect()
    {
        var requests = new List<HttpRequestMessage>();
        var inner = new StubHandler(request =>
        {
            requests.Add(request);
            return requests.Count == 1
                ? Redirect("/v1/images/primary/JavDB/ABC-123?retry=1")
                : new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = CreateClient(inner);
        using var request = CreateAuthenticatedRequest("http://metax.test/v1/images/primary/JavDB/ABC-123");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, requests.Count);
        Assert.True(requests[1].Headers.Contains(MetaXApiClient.ProviderCookiesHeaderName));
        Assert.Equal("Bearer test-token", requests[1].Headers.Authorization?.ToString());
    }

    [Fact]
    public async Task SendAsync_RejectsTooManyRedirects()
    {
        var inner = new StubHandler(_ => Redirect("/loop"));

        using var client = CreateClient(inner);
        using var request = CreateAuthenticatedRequest("http://metax.test/loop");

        await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync(request));
    }

    [Fact]
    public void IsMetaXServerUrl_RequiresExactOriginAndBasePath()
    {
        Assert.True(MetaXApiClient.IsMetaXServerUrl("http://metax.test", "http://metax.test/v1/images/1"));
        Assert.True(MetaXApiClient.IsMetaXServerUrl("http://metax.test/base", "http://metax.test/base/v1/images/1"));
        Assert.False(MetaXApiClient.IsMetaXServerUrl("http://metax.test", "https://metax.test/v1/images/1"));
        Assert.False(MetaXApiClient.IsMetaXServerUrl("http://metax.test", "http://metax.test.evil/v1/images/1"));
        Assert.False(MetaXApiClient.IsMetaXServerUrl("http://metax.test/base", "http://metax.test/other/v1/images/1"));
    }

    private static HttpClient CreateClient(HttpMessageHandler inner)
    {
        return new HttpClient(new SafeRedirectHandler(
            inner,
            url => MetaXApiClient.IsMetaXServerUrl("http://metax.test", url),
            NullLogger.Instance));
    }

    private static HttpRequestMessage CreateAuthenticatedRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation(MetaXApiClient.ProviderCookiesHeaderName,
            """{"JavDB":"session=secret"}""");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return request;
    }

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Redirect);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }
    }
}
