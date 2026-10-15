using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using BancontactPro.Signing;
using Microsoft.Extensions.Options;

namespace BancontactPro.Net.Tests.Signing;

public class JwksClientTests
{
    private sealed class FakeJwksHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public Func<object> ResponseFactory { get; set; } = () => new { keys = Array.Empty<object>() };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(ResponseFactory()),
            };
            return Task.FromResult(response);
        }
    }

    private static string Base64UrlOf(byte[] bytes) => Base64Url.EncodeToString(bytes);

    private static object JwkFor(ECDsa key, string kid)
    {
        var p = key.ExportParameters(includePrivateParameters: false);
        return new
        {
            kty = "EC",
            crv = "P-256",
            kid,
            x = Base64UrlOf(p.Q.X!),
            y = Base64UrlOf(p.Q.Y!),
        };
    }

    private static (JwksClient client, FakeJwksHandler handler) CreateClient()
    {
        var handler = new FakeJwksHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://jwks.preprod.bancontact.net") };
        var options = Options.Create(new JwksClientOptions { JwksUri = new Uri("https://jwks.preprod.bancontact.net/") });
        return (new JwksClient(httpClient, options), handler);
    }

    [Fact]
    public async Task GetKeyAsync_CacheHit_DoesNotRefetch()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (client, handler) = CreateClient();
        handler.ResponseFactory = () => new { keys = new[] { JwkFor(key, "kid-1") } };

        var first = await client.GetKeyAsync("kid-1");
        var second = await client.GetKeyAsync("kid-1");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetKeyAsync_CacheMiss_TriggersExactlyOneRefresh()
    {
        var (client, handler) = CreateClient();
        handler.ResponseFactory = () => new { keys = Array.Empty<object>() };

        await client.GetKeyAsync("missing-kid");

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetKeyAsync_KeyAddedByRotation_IsFoundOnNextMiss()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (client, handler) = CreateClient();
        handler.ResponseFactory = () => new { keys = Array.Empty<object>() };

        var beforeRotation = await client.GetKeyAsync("new-kid");
        Assert.Null(beforeRotation);
        Assert.Equal(1, handler.RequestCount);

        handler.ResponseFactory = () => new { keys = new[] { JwkFor(key, "new-kid") } };
        var afterRotation = await client.GetKeyAsync("new-kid");

        Assert.NotNull(afterRotation);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task GetKeyAsync_UnknownKidAfterRefresh_ReturnsNull()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (client, handler) = CreateClient();
        handler.ResponseFactory = () => new { keys = new[] { JwkFor(key, "some-other-kid") } };

        var result = await client.GetKeyAsync("requested-kid");

        Assert.Null(result);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetKeyAsync_SkipsNonEcAndNonP256KeysWithoutThrowing()
    {
        using var validKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (client, handler) = CreateClient();
        handler.ResponseFactory = () => new
        {
            keys = new object[]
            {
                new { kty = "RSA", kid = "rsa-kid", n = "abc", e = "AQAB" },
                JwkFor(validKey, "valid-kid"),
            },
        };

        var validResult = await client.GetKeyAsync("valid-kid");
        var rsaResult = await client.GetKeyAsync("rsa-kid");

        Assert.NotNull(validResult);
        Assert.Null(rsaResult);
    }
}
