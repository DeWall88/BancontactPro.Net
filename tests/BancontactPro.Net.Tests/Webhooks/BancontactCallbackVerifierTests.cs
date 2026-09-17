using System.Security.Cryptography;
using BancontactPro.Models;
using BancontactPro.Signing;
using BancontactPro.Webhooks;

namespace BancontactPro.Net.Tests.Webhooks;

public class BancontactCallbackVerifierTests
{
    private sealed class FakeJwksClient : IJwksClient
    {
        private readonly Dictionary<string, ECDsa> _keys = new();

        public void Add(string kid, ECDsa key) => _keys[kid] = key;

        public Task<ECDsa?> GetKeyAsync(string kid, CancellationToken cancellationToken = default) =>
            Task.FromResult(_keys.TryGetValue(kid, out var key) ? key : null);
    }

    private const string ValidBody = """
    {
      "paymentId": "payment-123",
      "currency": "EUR",
      "amount": 1000,
      "description": "Order #42",
      "reference": "ref-1",
      "createdAt": "2026-01-02T03:04:05.000Z",
      "expireAt": "2026-01-02T03:19:05.000Z",
      "succeededAt": null,
      "status": "AUTHORIZED",
      "debtor": { "iban": "BE**********1234", "name": "Jane Doe" }
    }
    """;

    private static JoseHeader SampleHeader(string path = "/callback") => new(
        Kid: "bancontact-kid",
        MerchantProfileId: "merchant-123",
        Issuer: "Payconiq",
        IssuedAt: DateTimeOffset.UtcNow,
        RequestId: "jti-xyz",
        Path: path);

    private static (BancontactCallbackVerifier verifier, ECDsa key) CreateVerifier()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jwks = new FakeJwksClient();
        jwks.Add("bancontact-kid", key);
        var verifier = new BancontactCallbackVerifier(new BancontactSignatureVerifier(jwks));
        return (verifier, key);
    }

    [Fact]
    public async Task VerifyAndParseAsync_ValidCallback_ReturnsParsedPayloadAndRequestId()
    {
        var (verifier, key) = CreateVerifier();
        var body = System.Text.Encoding.UTF8.GetBytes(ValidBody);
        var signature = DetachedJwsSigner.Sign(SampleHeader(), body, key);

        var result = await verifier.VerifyAndParseAsync(signature, body, "/callback");

        Assert.Equal("jti-xyz", result.RequestId);
        Assert.Equal("payment-123", result.Payload.PaymentId);
        Assert.Equal(1000, result.Payload.Amount);
        Assert.Equal("EUR", result.Payload.Currency);
        Assert.Equal(MerchantPaymentStatus.AUTHORIZED, result.Payload.Status);
        Assert.Equal("BE**********1234", result.Payload.Debtor.Iban);
        Assert.Equal("Jane Doe", result.Payload.Debtor.Name);
        Assert.Null(result.Payload.SucceededAt);
    }

    [Fact]
    public async Task VerifyAndParseAsync_InvalidSignature_Throws()
    {
        var (verifier, _) = CreateVerifier();
        using var wrongKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var body = System.Text.Encoding.UTF8.GetBytes(ValidBody);
        var signature = DetachedJwsSigner.Sign(SampleHeader(), body, wrongKey);

        await Assert.ThrowsAsync<InvalidCallbackException>(() => verifier.VerifyAndParseAsync(signature, body, "/callback"));
    }

    [Fact]
    public async Task VerifyAndParseAsync_TamperedAmount_Throws()
    {
        var (verifier, key) = CreateVerifier();
        var signedBody = System.Text.Encoding.UTF8.GetBytes(ValidBody);
        var signature = DetachedJwsSigner.Sign(SampleHeader(), signedBody, key);

        var tamperedBody = System.Text.Encoding.UTF8.GetBytes(ValidBody.Replace("\"amount\": 1000", "\"amount\": 999999"));

        await Assert.ThrowsAsync<InvalidCallbackException>(() => verifier.VerifyAndParseAsync(signature, tamperedBody, "/callback"));
    }

    [Fact]
    public async Task VerifyAndParseAsync_MalformedBody_ThrowsInvalidCallbackException()
    {
        var (verifier, key) = CreateVerifier();
        var body = System.Text.Encoding.UTF8.GetBytes("not json");
        var signature = DetachedJwsSigner.Sign(SampleHeader(), body, key);

        await Assert.ThrowsAsync<InvalidCallbackException>(() => verifier.VerifyAndParseAsync(signature, body, "/callback"));
    }
}
