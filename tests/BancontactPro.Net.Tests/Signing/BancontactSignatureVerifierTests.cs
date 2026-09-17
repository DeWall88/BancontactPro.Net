using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using BancontactPro.Signing;

namespace BancontactPro.Net.Tests.Signing;

public class BancontactSignatureVerifierTests
{
    private sealed class FakeJwksClient : IJwksClient
    {
        private readonly Dictionary<string, ECDsa> _keys = new();

        public void Add(string kid, ECDsa key) => _keys[kid] = key;

        public Task<ECDsa?> GetKeyAsync(string kid, CancellationToken cancellationToken = default) =>
            Task.FromResult(_keys.TryGetValue(kid, out var key) ? key : null);
    }

    private static JoseHeader SampleHeader(string path = "/callback") => new(
        Kid: "bancontact-kid",
        MerchantProfileId: "merchant-123",
        Issuer: "Payconiq",
        IssuedAt: new DateTimeOffset(2026, 1, 2, 3, 4, 5, 678, TimeSpan.Zero),
        RequestId: "jti-abc",
        Path: path);

    private static string BuildRawSignature(string headerJson, ReadOnlySpan<byte> body, ECDsa key)
    {
        var encodedHeader = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(headerJson));
        var signingInput = Encoding.ASCII.GetBytes($"{encodedHeader}.{Base64Url.EncodeToString(body)}");
        var signature = key.SignData(signingInput, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{encodedHeader}.{Base64Url.EncodeToString(signature)}";
    }

    [Fact]
    public async Task VerifyAsync_ValidCallback_ReturnsClaims()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jwks = new FakeJwksClient();
        jwks.Add("bancontact-kid", key);
        var verifier = new BancontactSignatureVerifier(jwks);
        var body = """{"paymentId":"p1"}"""u8.ToArray();

        var signature = DetachedJwsSigner.Sign(SampleHeader(), body, key);
        var claims = await verifier.VerifyAsync(signature, body, "/callback");

        Assert.NotNull(claims);
        Assert.Equal("merchant-123", claims!.MerchantProfileId);
        Assert.Equal("Payconiq", claims.Issuer);
        Assert.Equal("jti-abc", claims.RequestId);
        Assert.Equal("/callback", claims.Path);
    }

    [Fact]
    public async Task VerifyAsync_TamperedBody_ReturnsNull()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jwks = new FakeJwksClient();
        jwks.Add("bancontact-kid", key);
        var verifier = new BancontactSignatureVerifier(jwks);

        var signature = DetachedJwsSigner.Sign(SampleHeader(), """{"amount":100}"""u8, key);
        var claims = await verifier.VerifyAsync(signature, """{"amount":999}"""u8.ToArray(), "/callback");

        Assert.Null(claims);
    }

    [Fact]
    public async Task VerifyAsync_UnknownKid_ReturnsNull()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jwks = new FakeJwksClient(); // no keys registered
        var verifier = new BancontactSignatureVerifier(jwks);

        var signature = DetachedJwsSigner.Sign(SampleHeader(), "{}"u8, key);
        var claims = await verifier.VerifyAsync(signature, "{}"u8.ToArray(), "/callback");

        Assert.Null(claims);
    }

    [Fact]
    public async Task VerifyAsync_PathMismatch_ReturnsNull()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jwks = new FakeJwksClient();
        jwks.Add("bancontact-kid", key);
        var verifier = new BancontactSignatureVerifier(jwks);

        var signature = DetachedJwsSigner.Sign(SampleHeader(path: "/callback"), "{}"u8, key);
        var claims = await verifier.VerifyAsync(signature, "{}"u8.ToArray(), "/some/other/path");

        Assert.Null(claims);
    }

    [Fact]
    public async Task VerifyAsync_UnrecognizedCritEntry_IsRejected()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jwks = new FakeJwksClient();
        jwks.Add("bancontact-kid", key);
        var verifier = new BancontactSignatureVerifier(jwks);
        var body = "{}"u8.ToArray();

        const string headerJson = """
        {
          "typ": "jose+json",
          "kid": "bancontact-kid",
          "alg": "ES256",
          "https://payconiq.com/sub": "merchant-123",
          "https://payconiq.com/iss": "Payconiq",
          "https://payconiq.com/iat": "2026-01-02T03:04:05.678Z",
          "https://payconiq.com/jti": "jti-abc",
          "https://payconiq.com/path": "/callback",
          "https://payconiq.com/evil": "injected",
          "crit": ["https://payconiq.com/sub", "https://payconiq.com/iss", "https://payconiq.com/iat", "https://payconiq.com/jti", "https://payconiq.com/path", "https://payconiq.com/evil"]
        }
        """;

        var signature = BuildRawSignature(headerJson, body, key);
        var claims = await verifier.VerifyAsync(signature, body, "/callback");

        Assert.Null(claims);
    }

    [Fact]
    public async Task VerifyAsync_MissingRequiredClaim_ReturnsNullWithoutThrowing()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jwks = new FakeJwksClient();
        jwks.Add("bancontact-kid", key);
        var verifier = new BancontactSignatureVerifier(jwks);
        var body = "{}"u8.ToArray();

        const string headerJson = """
        {
          "typ": "jose+json",
          "kid": "bancontact-kid",
          "alg": "ES256",
          "https://payconiq.com/iss": "Payconiq",
          "https://payconiq.com/iat": "2026-01-02T03:04:05.678Z",
          "https://payconiq.com/jti": "jti-abc",
          "https://payconiq.com/path": "/callback",
          "crit": ["https://payconiq.com/sub", "https://payconiq.com/iss", "https://payconiq.com/iat", "https://payconiq.com/jti", "https://payconiq.com/path"]
        }
        """;

        var signature = BuildRawSignature(headerJson, body, key);
        var claims = await verifier.VerifyAsync(signature, body, "/callback");

        Assert.Null(claims);
    }

    [Theory]
    [InlineData("not-a-valid-signature-value")]
    [InlineData("one.two.three")]
    public async Task VerifyAsync_MalformedSignatureValue_ReturnsNull(string malformed)
    {
        var jwks = new FakeJwksClient();
        var verifier = new BancontactSignatureVerifier(jwks);

        var claims = await verifier.VerifyAsync(malformed, "{}"u8.ToArray(), "/callback");

        Assert.Null(claims);
    }
}
