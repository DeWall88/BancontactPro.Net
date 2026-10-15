using System.Net;
using BancontactPro.Http;

namespace BancontactPro.Net.Tests.Http;

public class BancontactApiKeyHandlerTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    [Fact]
    public async Task SendAsync_AttachesBearerApiKeyAndNoSignature()
    {
        var inner = new CapturingHandler();
        using var client = new HttpClient(new BancontactApiKeyHandler("api-key") { InnerHandler = inner });

        await client.PostAsync("https://merchant.api.preprod.bancontact.net/v3/payments", new StringContent("{}"));

        Assert.Equal("Bearer", inner.LastRequest!.Headers.Authorization?.Scheme);
        Assert.Equal("api-key", inner.LastRequest.Headers.Authorization?.Parameter);
        Assert.False(inner.LastRequest.Headers.Contains("Signature"));
    }

    [Fact]
    public async Task SendAsync_JsonBody_IsSentWithAContentLength()
    {
        var inner = new CapturingHandler();
        using var client = new HttpClient(new BancontactApiKeyHandler("api-key") { InnerHandler = inner });

        // JsonContent streams by default and can't report its length up front: without buffering it would go out chunked.
        await client.PostAsync("https://merchant.api.preprod.bancontact.net/v3/payments", System.Net.Http.Json.JsonContent.Create(new { amount = 100 }));

        Assert.Equal("""{"amount":100}""".Length, inner.LastRequest!.Content!.Headers.ContentLength);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_MissingApiKey_Throws(string? apiKey)
    {
        Assert.ThrowsAny<ArgumentException>(() => new BancontactApiKeyHandler(apiKey!));
    }
}
