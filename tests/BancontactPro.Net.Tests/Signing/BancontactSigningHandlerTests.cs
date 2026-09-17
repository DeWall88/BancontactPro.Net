using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BancontactPro.Signing;
using Microsoft.Extensions.Options;

namespace BancontactPro.Net.Tests.Signing;

public class BancontactSigningHandlerTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public byte[] LastRequestBody { get; private set; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content is not null
                ? await request.Content.ReadAsByteArrayAsync(cancellationToken)
                : [];
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class FakeKeyProvider : IRequestSigningKeyProvider
    {
        public string Kid { get; } = "test-kid";
        public ECDsa PrivateKey { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    }

    private static (HttpClient client, CapturingHandler inner, FakeKeyProvider keyProvider) CreateClient(BancontactSigningOptions? options = null)
    {
        var keyProvider = new FakeKeyProvider();
        var inner = new CapturingHandler();
        var signingHandler = new BancontactSigningHandler(keyProvider, Options.Create(options ?? new BancontactSigningOptions
        {
            ApiKey = "test-api-key",
            MerchantProfileId = "merchant-123",
        }))
        {
            InnerHandler = inner,
        };

        return (new HttpClient(signingHandler), inner, keyProvider);
    }

    [Fact]
    public async Task SendAsync_SetsBearerAuthorizationHeader()
    {
        var (client, inner, _) = CreateClient(new BancontactSigningOptions { ApiKey = "my-key", MerchantProfileId = "m-1" });

        await client.GetAsync("https://merchant.api.preprod.bancontact.net/v3/payments/abc");

        Assert.Equal("Bearer", inner.LastRequest!.Headers.Authorization?.Scheme);
        Assert.Equal("my-key", inner.LastRequest.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task SendAsync_SetsSignatureHeaderThatVerifiesAgainstSentBody()
    {
        var (client, inner, keyProvider) = CreateClient();
        var content = new StringContent("""{"amount":100}""", Encoding.UTF8, "application/json");

        await client.PostAsync("https://merchant.api.preprod.bancontact.net/v3/payments", content);

        var request = inner.LastRequest!;
        var signature = Assert.Single(request.Headers.GetValues("Signature"));
        Assert.Equal(1, signature.Count(c => c == '.'));
        Assert.True(DetachedJwsSigner.Verify(signature, inner.LastRequestBody, keyProvider.PrivateKey));
    }

    [Fact]
    public async Task SendAsync_SignsEmptyBodyForBodylessRequests()
    {
        var (client, inner, keyProvider) = CreateClient();

        await client.GetAsync("https://merchant.api.preprod.bancontact.net/v3/payments/abc123");

        var signature = Assert.Single(inner.LastRequest!.Headers.GetValues("Signature"));
        Assert.True(DetachedJwsSigner.Verify(signature, ReadOnlySpan<byte>.Empty, keyProvider.PrivateKey));
    }

    [Fact]
    public async Task SendAsync_SignsExpectedPathAndClaims()
    {
        var (client, inner, _) = CreateClient(new BancontactSigningOptions { ApiKey = "k", MerchantProfileId = "merchant-xyz" });

        await client.GetAsync("https://merchant.api.preprod.bancontact.net/v3/payments/abc123?foo=bar");

        var signature = Assert.Single(inner.LastRequest!.Headers.GetValues("Signature"));
        var encodedHeader = signature[..signature.IndexOf('.')];
        var headerJson = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(encodedHeader));
        using var doc = JsonDocument.Parse(headerJson);

        Assert.Equal("/v3/payments/abc123", doc.RootElement.GetProperty("https://payconiq.com/path").GetString());
        Assert.Equal("merchant-xyz", doc.RootElement.GetProperty("https://payconiq.com/sub").GetString());
        Assert.Equal("Payconiq", doc.RootElement.GetProperty("https://payconiq.com/iss").GetString());
    }

    [Fact]
    public async Task SendAsync_UsesDistinctRequestIdsAcrossCalls()
    {
        var (client, inner, _) = CreateClient();

        await client.GetAsync("https://merchant.api.preprod.bancontact.net/v3/payments/a");
        var first = Assert.Single(inner.LastRequest!.Headers.GetValues("Signature"));

        await client.GetAsync("https://merchant.api.preprod.bancontact.net/v3/payments/b");
        var second = Assert.Single(inner.LastRequest!.Headers.GetValues("Signature"));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task SendAsync_HonorsCustomIssuer()
    {
        var (client, inner, _) = CreateClient(new BancontactSigningOptions
        {
            ApiKey = "k",
            MerchantProfileId = "m",
            Issuer = "merchant-id-override",
        });

        await client.GetAsync("https://merchant.api.preprod.bancontact.net/v3/reconciliations/search");

        var signature = Assert.Single(inner.LastRequest!.Headers.GetValues("Signature"));
        var encodedHeader = signature[..signature.IndexOf('.')];
        var headerJson = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(encodedHeader));
        using var doc = JsonDocument.Parse(headerJson);

        Assert.Equal("merchant-id-override", doc.RootElement.GetProperty("https://payconiq.com/iss").GetString());
    }
}
