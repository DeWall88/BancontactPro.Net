using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BancontactPro.Signing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BancontactPro.Sample.Api.Tests;

/// <summary>
/// The sample against a fake Bancontact: what each route sends on the wire, and what comes back. Tests in one class
/// run one at a time, so the shared fake is reset per test.
/// </summary>
public sealed class SampleApiTests : IClassFixture<SampleApiTests.SampleApiFactory>
{
    private const string Kid = "bancontact-kid";
    private const string SampleKey = "test-sample-key";

    private readonly SampleApiFactory _factory;

    public SampleApiTests(SampleApiFactory factory)
    {
        _factory = factory;
        _factory.Bancontact.Reset();
    }

    public sealed class SampleApiFactory : WebApplicationFactory<Program>
    {
        public ECDsa BancontactKey { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public FakeBancontact Bancontact { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Two products with their API key and PPID; no signing key, as for payments only.
            builder.UseSetting("BancontactPro:Products:OnDisplay:ApiKey", "on-display-key");
            builder.UseSetting("BancontactPro:Products:OnDisplay:MerchantProfileId", "on-display-ppid");
            builder.UseSetting("BancontactPro:Products:StaticQr:ApiKey", "static-qr-key");
            builder.UseSetting("BancontactPro:Products:StaticQr:MerchantProfileId", "static-qr-ppid");
            builder.UseSetting("SampleApiKey", SampleKey);
            builder.UseSetting("PublicBaseUrl", "https://sample.example.com");

            builder.ConfigureServices(services =>
            {
                // Bancontact's public signing key, standing in for the JWKS endpoint.
                services.AddSingleton<IJwksClient>(new FakeJwksClient(Kid, BancontactKey));
                // Every outgoing Bancontact call lands in the fake instead of the network.
                services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => Bancontact));
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                BancontactKey.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>Records what the library sends and answers with whatever the test queued.</summary>
    public sealed class FakeBancontact : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

        public HttpStatusCode NextStatus { get; set; } = HttpStatusCode.OK;

        public string NextBody { get; set; } = "";

        public void Reset()
        {
            Requests.Clear();
            NextStatus = HttpStatusCode.OK;
            NextBody = "";
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(NextStatus) { Content = new StringContent(NextBody, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class FakeJwksClient(string kid, ECDsa key) : IJwksClient
    {
        public Task<ECDsa?> GetKeyAsync(string requestedKid, CancellationToken cancellationToken = default) =>
            Task.FromResult<ECDsa?>(requestedKid == kid ? key : null);
    }

    private const string CreatedPaymentJson = """
        {
          "paymentId": "p-123", "status": "PENDING",
          "createdAt": "2026-01-02T03:04:05.000Z", "expiresAt": "2026-01-02T03:06:05.000Z",
          "amount": 100, "currency": "EUR", "creditor": {},
          "_links": {
            "self": { "href": "https://merchant.api.preprod.bancontact.net/v3/payments/p-123" },
            "deeplink": { "href": "https://pay.example/deeplink" },
            "qrcode": { "href": "https://qr.example/p-123" }
          }
        }
        """;

    private const string PaymentJson = """
        {
          "paymentId": "p-123", "status": "SUCCEEDED",
          "createdAt": "2026-01-02T03:04:05.000Z", "expireAt": "2026-01-02T03:06:05.000Z",
          "currency": "EUR", "creditor": {}, "amount": 100,
          "_links": { "self": { "href": "x" }, "deeplink": { "href": "x" }, "qrcode": { "href": "x" } }
        }
        """;

    private HttpClient Client(bool withKey = true)
    {
        var client = _factory.CreateClient();
        if (withKey)
        {
            client.DefaultRequestHeaders.Add(SampleEndpoints.ApiKeyHeader, SampleKey);
        }

        return client;
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private (HttpRequestMessage Request, string? Body) SentToBancontact => Assert.Single(_factory.Bancontact.Requests);

    // --- Routes map one-to-one onto Bancontact's endpoints ---

    [Fact]
    public async Task CreatePayment_ForwardsToPostV3Payments_WithThatProductsApiKey()
    {
        _factory.Bancontact.NextStatus = HttpStatusCode.Created;
        _factory.Bancontact.NextBody = CreatedPaymentJson;

        var response = await Client().PostAsync("/OnDisplay/v3/payments", Json("""{"amount":100,"description":"Test"}"""));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("\"paymentId\":\"p-123\"", await response.Content.ReadAsStringAsync());
        var (request, body) = SentToBancontact;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://merchant.api.preprod.bancontact.net/v3/payments", request.RequestUri!.ToString());
        Assert.Equal("Bearer on-display-key", request.Headers.Authorization!.ToString());
        Assert.False(request.Headers.Contains("Signature"));
        // Only what was set, plus the sample's default callback: no nulls on the wire.
        Assert.Equal("""{"amount":100,"description":"Test","callbackUrl":"https://sample.example.com/bancontact/callback"}""", body);
    }

    [Fact]
    public async Task CreatePayment_KeepsAnExplicitCallbackUrl()
    {
        _factory.Bancontact.NextStatus = HttpStatusCode.Created;
        _factory.Bancontact.NextBody = CreatedPaymentJson;

        await Client().PostAsync("/OnDisplay/v3/payments", Json("""{"amount":100,"callbackUrl":"https://elsewhere.example.com/cb"}"""));

        Assert.Contains("\"callbackUrl\":\"https://elsewhere.example.com/cb\"", SentToBancontact.Body);
    }

    [Fact]
    public async Task CreatePosPayment_ForwardsToPostV3PaymentsPos_WithThatProductsApiKey()
    {
        _factory.Bancontact.NextStatus = HttpStatusCode.Created;
        _factory.Bancontact.NextBody = CreatedPaymentJson;

        var response = await Client().PostAsync("/StaticQr/v3/payments/pos", Json("""{"amount":100,"posId":"till-1"}"""));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var (request, body) = SentToBancontact;
        Assert.Equal("/v3/payments/pos", request.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer static-qr-key", request.Headers.Authorization!.ToString());
        Assert.Contains("\"posId\":\"till-1\"", body);
    }

    [Fact]
    public async Task ProductNames_AreCaseInsensitive()
    {
        _factory.Bancontact.NextBody = PaymentJson;

        var response = await Client().GetAsync("/ondisplay/v3/payments/p-123");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetPayment_ForwardsToGetV3Payments()
    {
        _factory.Bancontact.NextBody = PaymentJson;

        var response = await Client().GetAsync("/OnDisplay/v3/payments/p-123");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"status\":\"SUCCEEDED\"", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpMethod.Get, SentToBancontact.Request.Method);
        Assert.Equal("/v3/payments/p-123", SentToBancontact.Request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task CancelPayment_ForwardsToDeleteV3Payments()
    {
        _factory.Bancontact.NextStatus = HttpStatusCode.NoContent;

        var response = await Client().DeleteAsync("/OnDisplay/v3/payments/p-123");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpMethod.Delete, SentToBancontact.Request.Method);
        Assert.Equal("/v3/payments/p-123", SentToBancontact.Request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task SearchPayments_ForwardsPageAndSize()
    {
        _factory.Bancontact.NextBody = """{"size":0,"totalPages":0,"totalElements":0,"number":0,"details":[]}""";

        await Client().PostAsync("/OnDisplay/v3/payments/search?page=2&size=5", Json("""{"reference":"r-1"}"""));

        Assert.Equal("/v3/payments/search?page=2&size=5", SentToBancontact.Request.RequestUri!.PathAndQuery);
        Assert.Equal("""{"reference":"r-1"}""", SentToBancontact.Body);
    }

    [Fact]
    public async Task QrPaymentUrl_IsBuiltWithThatProductsPpid_WithoutCallingBancontact()
    {
        var response = await Client().GetAsync("/OnDisplay/qr/payment-url?amount=1000&description=Receipt%20Payment&reference=r-1&size=XL");
        using var links = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // No Environment configured: Preprod, the library's default.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("https://pay.acc.bancontact.net/t/1/on-display-ppid?D=Receipt%20Payment&A=1000&R=r-1", links.RootElement.GetProperty("payloadUrl").GetString());
        Assert.StartsWith("https://qrcodegenerator.api.preprod.bancontact.net/qrcode?f=PNG&s=XL&c=", links.RootElement.GetProperty("qrCodeImageUrl").GetString());
        Assert.Empty(_factory.Bancontact.Requests);
    }

    [Fact]
    public async Task QrPosSticker_IsBuiltWithThatProductsPpid()
    {
        var response = await Client().GetAsync("/StaticQr/qr/pos/till-1?format=svg");
        using var links = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal("https://pay.acc.bancontact.net/l/1/static-qr-ppid/till-1", links.RootElement.GetProperty("payloadUrl").GetString());
        Assert.StartsWith("https://qrcodegenerator.api.preprod.bancontact.net/qrcode?f=SVG&c=", links.RootElement.GetProperty("qrCodeImageUrl").GetString());
    }

    [Fact]
    public async Task QrCodes_InProduction_UseTheProductionHosts()
    {
        using var production = _factory.WithWebHostBuilder(builder => builder.UseSetting("BancontactPro:Environment", "Production"));
        var client = production.CreateClient();
        client.DefaultRequestHeaders.Add(SampleEndpoints.ApiKeyHeader, SampleKey);

        using var payment = JsonDocument.Parse(await client.GetStringAsync("/OnDisplay/qr/payment-url?amount=1000"));
        using var sticker = JsonDocument.Parse(await client.GetStringAsync("/StaticQr/qr/pos/till-1"));

        Assert.Equal("https://pay.bancontact.net/t/1/on-display-ppid?A=1000", payment.RootElement.GetProperty("payloadUrl").GetString());
        Assert.StartsWith("https://qrcodegenerator.api.bancontact.net/qrcode?f=PNG&c=", payment.RootElement.GetProperty("qrCodeImageUrl").GetString());
        Assert.Equal("https://pay.bancontact.net/l/1/static-qr-ppid/till-1", sticker.RootElement.GetProperty("payloadUrl").GetString());
        Assert.StartsWith("https://qrcodegenerator.api.bancontact.net/qrcode?f=PNG&c=", sticker.RootElement.GetProperty("qrCodeImageUrl").GetString());
    }

    // --- Errors say why ---

    [Fact]
    public async Task BancontactError_IsPassedThroughWithItsStatusCodeAndTraceIds()
    {
        _factory.Bancontact.NextStatus = HttpStatusCode.Unauthorized;
        _factory.Bancontact.NextBody = """{"code":"UNAUTHORIZED","message":"Invalid API key","traceId":"trace-1","spanId":"span-1"}""";

        var response = await Client().PostAsync("/OnDisplay/v3/payments", Json("""{"amount":100}"""));
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("UNAUTHORIZED", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal("Invalid API key", problem.RootElement.GetProperty("detail").GetString());
        Assert.Equal("trace-1", problem.RootElement.GetProperty("traceId").GetString());
        Assert.Equal("span-1", problem.RootElement.GetProperty("spanId").GetString());
        Assert.Contains("not the PPID", problem.RootElement.GetProperty("hint").GetString());
    }

    // What preprod actually returns for an unknown API key: not Bancontact's {code, message} shape.
    [Fact]
    public async Task BancontactUnauthorizedInGatewayShape_KeepsTheBodyAndHints()
    {
        _factory.Bancontact.NextStatus = HttpStatusCode.Unauthorized;
        _factory.Bancontact.NextBody = """{"timestamp":"2026-10-15T21:27:32.898Z","path":"/v3/payments","status":401,"error":"Unauthorized","requestId":"r-1"}""";

        var response = await Client().PostAsync("/OnDisplay/v3/payments", Json("""{"amount":100}"""));
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("\"requestId\":\"r-1\"", problem.RootElement.GetProperty("detail").GetString());
        Assert.True(problem.RootElement.TryGetProperty("hint", out _));
    }

    [Fact]
    public async Task BancontactErrorWithoutBody_StillSaysWhatHappened()
    {
        _factory.Bancontact.NextStatus = HttpStatusCode.BadGateway;

        var response = await Client().GetAsync("/OnDisplay/v3/payments/p-123");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("Bancontact returned 502", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnconfiguredProduct_Returns400NamingTheSettings_AndSendsNothing()
    {
        var response = await Client().PostAsync("/TopUp/v3/payments", Json("""{"amount":100}"""));
        var problem = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Product TopUp is not configured", problem);
        Assert.Contains("Products:TopUp:ApiKey", problem);
        Assert.Empty(_factory.Bancontact.Requests);
    }

    [Fact]
    public async Task UnknownProduct_Returns404ListingTheProducts()
    {
        var response = await Client().GetAsync("/Nonsense/v3/payments/p-123");
        var problem = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("OnDisplay, OnReceipt, StaticQr, TopUp, Invoice", problem);
    }

    [Fact]
    public async Task Refund_WithoutSigningKey_Returns400_AndSendsNothing()
    {
        var client = Client();
        client.DefaultRequestHeaders.Add("Idempotency-Key", "k-1");

        var response = await client.PostAsync("/OnDisplay/v3/payments/p-123/refunds", Json("""{"amount":100,"currency":"EUR"}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("No signing key configured", await response.Content.ReadAsStringAsync());
        Assert.Empty(_factory.Bancontact.Requests);
    }

    [Fact]
    public async Task Refund_WithoutIdempotencyKey_Returns400()
    {
        var response = await Client().PostAsync("/OnDisplay/v3/payments/p-123/refunds", Json("""{"amount":100,"currency":"EUR"}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Idempotency-Key", await response.Content.ReadAsStringAsync());
    }

    // --- Access ---

    [Fact]
    public async Task Health_NeedsNoKey()
    {
        var response = await Client(withKey: false).GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/callbacks")]
    [InlineData("GET", "/OnDisplay/v3/payments/p-123")]
    [InlineData("DELETE", "/OnDisplay/v3/payments/p-123")]
    [InlineData("POST", "/OnDisplay/v3/payments")]
    [InlineData("GET", "/OnDisplay/qr/payment-url")]
    public async Task EverythingButHealthAndCallback_NeedsTheKey(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = method == "POST" ? Json("""{"amount":100}""") : null };

        var response = await Client(withKey: false).SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_factory.Bancontact.Requests);
    }

    // --- Callbacks ---

    private static string CallbackBody(string paymentId, string status) => $$"""
        {
          "paymentId": "{{paymentId}}", "currency": "EUR", "amount": 100, "description": "Test", "reference": "r-1",
          "createdAt": "2026-01-02T03:04:05.000Z", "expireAt": "2026-01-02T03:06:05.000Z", "succeededAt": null,
          "status": "{{status}}", "debtor": { "iban": "BE**********1234", "name": "Jane Doe" }
        }
        """;

    private string Sign(string body, string path = SampleEndpoints.CallbackPath, ECDsa? key = null) =>
        DetachedJwsSigner.Sign(
            new JoseHeader(Kid, "on-display-ppid", "Payconiq", DateTimeOffset.UtcNow, Guid.NewGuid().ToString(), path),
            Encoding.UTF8.GetBytes(body),
            key ?? _factory.BancontactKey);

    private async Task<HttpResponseMessage> PostCallbackAsync(string body, string signature)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, SampleEndpoints.CallbackPath) { Content = Json(body) };
        request.Headers.TryAddWithoutValidation("Signature", signature);
        return await Client(withKey: false).SendAsync(request);
    }

    private async Task<string> CallbacksFor(string paymentId) =>
        await Client().GetStringAsync($"/callbacks?paymentId={paymentId}");

    [Fact]
    public async Task Callback_ValidSignature_IsAcceptedAndListed()
    {
        var paymentId = $"p-{Guid.NewGuid():N}";
        var body = CallbackBody(paymentId, "SUCCEEDED");

        var response = await PostCallbackAsync(body, Sign(body));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var listed = await CallbacksFor(paymentId);
        Assert.Contains(paymentId, listed);
        Assert.Contains("\"status\":\"SUCCEEDED\"", listed);
    }

    [Fact]
    public async Task Callback_SignedWithAnotherKey_IsRejectedAndNotListed()
    {
        var paymentId = $"p-{Guid.NewGuid():N}";
        var body = CallbackBody(paymentId, "SUCCEEDED");
        using var attackerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var response = await PostCallbackAsync(body, Sign(body, key: attackerKey));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("[]", await CallbacksFor(paymentId));
    }

    [Fact]
    public async Task Callback_BodyTamperedAfterSigning_IsRejected()
    {
        var paymentId = $"p-{Guid.NewGuid():N}";
        var signature = Sign(CallbackBody(paymentId, "FAILED"));

        var response = await PostCallbackAsync(CallbackBody(paymentId, "SUCCEEDED"), signature);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Callback_SignedForADifferentPath_IsRejected()
    {
        var body = CallbackBody("p-1", "SUCCEEDED");

        var response = await PostCallbackAsync(body, Sign(body, path: "/somewhere/else"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Callback_WithoutSignature_IsRejected()
    {
        var response = await PostCallbackAsync(CallbackBody("p-1", "SUCCEEDED"), string.Empty);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
