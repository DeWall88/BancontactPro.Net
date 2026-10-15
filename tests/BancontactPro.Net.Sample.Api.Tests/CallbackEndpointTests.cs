using System.Net;
using System.Security.Cryptography;
using System.Text;
using BancontactPro.Helpers;
using BancontactPro.Models;
using BancontactPro.Signing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BancontactPro.Sample.Api.Tests;

public sealed class CallbackEndpointTests : IClassFixture<CallbackEndpointTests.SampleApiFactory>
{
    private const string Kid = "bancontact-kid";

    private readonly SampleApiFactory _factory;

    public CallbackEndpointTests(SampleApiFactory factory) => _factory = factory;

    public sealed class SampleApiFactory : WebApplicationFactory<Program>
    {
        public ECDsa BancontactKey { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // One product's API key and PPID, as the Payment API needs: no merchant signing key, so this also proves the app starts without one.
            builder.UseSetting("BancontactPro:Products:StaticQr:ApiKey", "test-api-key");
            builder.UseSetting("BancontactPro:Products:StaticQr:MerchantProfileId", "test-ppid");
            builder.UseSetting("SampleApiKey", "test-sample-key");

            // Bancontact's public signing key, standing in for the JWKS endpoint.
            builder.ConfigureServices(services => services.AddSingleton<IJwksClient>(new FakeJwksClient(Kid, BancontactKey)));
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

    private sealed class FakeJwksClient(string kid, ECDsa key) : IJwksClient
    {
        public Task<ECDsa?> GetKeyAsync(string requestedKid, CancellationToken cancellationToken = default) =>
            Task.FromResult<ECDsa?>(requestedKid == kid ? key : null);
    }

    private static string Body(string paymentId, string status) => $$"""
        {
          "paymentId": "{{paymentId}}",
          "currency": "EUR",
          "amount": 1000,
          "description": "Order #42",
          "reference": "ref-1",
          "createdAt": "2026-01-02T03:04:05.000Z",
          "expireAt": "2026-01-02T03:19:05.000Z",
          "succeededAt": null,
          "status": "{{status}}",
          "debtor": { "iban": "BE**********1234", "name": "Jane Doe" }
        }
        """;

    private string Sign(string body, string path = SampleEndpoints.CallbackPath, ECDsa? key = null) =>
        DetachedJwsSigner.Sign(
            new JoseHeader(Kid, "merchant-123", "Payconiq", DateTimeOffset.UtcNow, Guid.NewGuid().ToString(), path),
            Encoding.UTF8.GetBytes(body),
            key ?? _factory.BancontactKey);

    private async Task<HttpResponseMessage> PostCallbackAsync(string body, string signature)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, SampleEndpoints.CallbackPath)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Signature", signature);
        return await _factory.CreateClient().SendAsync(request);
    }

    private string NewPendingPayment()
    {
        var paymentId = $"payment-{Guid.NewGuid():N}";
        _factory.Services.GetRequiredService<IPaymentStore>()
            .Add(new PaymentRecord(paymentId, BancontactProduct.StaticQr, MerchantPaymentStatus.PENDING, 1000, "ref-1", null, DateTimeOffset.UtcNow.AddMinutes(-5)));
        return paymentId;
    }

    private MerchantPaymentStatus? StatusOf(string paymentId) =>
        _factory.Services.GetRequiredService<IPaymentStore>().Get(paymentId)?.Status;

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await _factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Callback_ValidSignature_UpdatesPaymentStatus()
    {
        var paymentId = NewPendingPayment();
        var body = Body(paymentId, "SUCCEEDED");

        var response = await PostCallbackAsync(body, Sign(body));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MerchantPaymentStatus.SUCCEEDED, StatusOf(paymentId));
    }

    [Fact]
    public async Task Callback_WrongKey_IsRejectedAndLeavesPaymentUntouched()
    {
        var paymentId = NewPendingPayment();
        var body = Body(paymentId, "SUCCEEDED");
        using var attackerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var response = await PostCallbackAsync(body, Sign(body, key: attackerKey));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(MerchantPaymentStatus.PENDING, StatusOf(paymentId));
    }

    [Fact]
    public async Task Callback_BodyTamperedAfterSigning_IsRejected()
    {
        var paymentId = NewPendingPayment();
        var signature = Sign(Body(paymentId, "FAILED"));

        var response = await PostCallbackAsync(Body(paymentId, "SUCCEEDED"), signature);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(MerchantPaymentStatus.PENDING, StatusOf(paymentId));
    }

    [Fact]
    public async Task Callback_SignedForADifferentPath_IsRejected()
    {
        var paymentId = NewPendingPayment();
        var body = Body(paymentId, "SUCCEEDED");

        var response = await PostCallbackAsync(body, Sign(body, path: "/somewhere/else"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(MerchantPaymentStatus.PENDING, StatusOf(paymentId));
    }

    [Fact]
    public async Task Callback_MissingSignatureHeader_IsRejected()
    {
        var paymentId = NewPendingPayment();

        var response = await PostCallbackAsync(Body(paymentId, "SUCCEEDED"), string.Empty);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Callback_Duplicate_IsAcknowledgedAndChangesNothing()
    {
        var paymentId = NewPendingPayment();
        var body = Body(paymentId, "SUCCEEDED");

        var first = await PostCallbackAsync(body, Sign(body));
        var second = await PostCallbackAsync(body, Sign(body));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(MerchantPaymentStatus.SUCCEEDED, StatusOf(paymentId));
    }

    [Fact]
    public async Task Callback_AfterTerminalStatus_DoesNotMoveThePaymentBackwards()
    {
        var paymentId = NewPendingPayment();
        var succeeded = Body(paymentId, "SUCCEEDED");
        var lateIdentified = Body(paymentId, "IDENTIFIED");

        await PostCallbackAsync(succeeded, Sign(succeeded));
        var response = await PostCallbackAsync(lateIdentified, Sign(lateIdentified));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MerchantPaymentStatus.SUCCEEDED, StatusOf(paymentId));
    }

    [Fact]
    public async Task Callback_ForUnknownPayment_IsAcknowledged()
    {
        var body = Body("never-created", "SUCCEEDED");

        var response = await PostCallbackAsync(body, Sign(body));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PaymentEndpoints_WithoutTheSharedKey_AreRefused()
    {
        var client = _factory.CreateClient();

        var create = await client.PostAsync("/payments", new StringContent("{\"amount\":100}", Encoding.UTF8, "application/json"));
        var get = await client.GetAsync("/payments/anything");
        var cancel = await client.DeleteAsync("/payments/anything");

        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, cancel.StatusCode);
    }

    [Fact]
    public async Task PaymentEndpoints_WithTheSharedKey_ServeStoredPayments()
    {
        var paymentId = NewPendingPayment();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(SampleEndpoints.ApiKeyHeader, "test-sample-key");

        var response = await client.GetAsync($"/payments/{paymentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"product\":\"StaticQr\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CreatePayment_ForAnUnconfiguredProduct_IsRefusedWithWhatIsMissing()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(SampleEndpoints.ApiKeyHeader, "test-sample-key");

        var response = await client.PostAsync("/payments", new StringContent("""{"product":"TopUp","amount":100}""", Encoding.UTF8, "application/json"));
        var problem = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Product TopUp is not configured", problem);
        Assert.Contains("Products:TopUp:ApiKey", problem);
    }
}
