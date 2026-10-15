using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BancontactPro.Signing;

namespace BancontactPro.Net.Tests.Signing;

public class DetachedJwsSignerTests
{
    private static JoseHeader SampleHeader() => new(
        Kid: "test-kid",
        MerchantProfileId: "merchant-profile-123",
        Issuer: "Payconiq",
        IssuedAt: new DateTimeOffset(2026, 1, 2, 3, 4, 5, 678, TimeSpan.Zero),
        RequestId: "request-id-abc",
        Path: "/v3/payments/abc123");

    [Fact]
    public void Sign_ProducesExactlyTwoSegments()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var body = "{}"u8.ToArray();

        var jws = DetachedJwsSigner.Sign(SampleHeader(), body, key);

        Assert.Equal(1, jws.Count(c => c == '.'));
    }

    [Fact]
    public void Sign_HeaderRoundTripsExpectedClaims()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var header = SampleHeader();

        var jws = DetachedJwsSigner.Sign(header, "{}"u8, key);
        var encodedHeader = jws[..jws.IndexOf('.')];
        var headerJson = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(encodedHeader));

        using var doc = JsonDocument.Parse(headerJson);
        var root = doc.RootElement;

        Assert.Equal("jose+json", root.GetProperty("typ").GetString());
        Assert.Equal("ES256", root.GetProperty("alg").GetString());
        Assert.Equal(header.Kid, root.GetProperty("kid").GetString());
        Assert.Equal(header.MerchantProfileId, root.GetProperty("https://payconiq.com/sub").GetString());
        Assert.Equal(header.Issuer, root.GetProperty("https://payconiq.com/iss").GetString());
        Assert.Equal("2026-01-02T03:04:05.678Z", root.GetProperty("https://payconiq.com/iat").GetString());
        Assert.Equal(header.RequestId, root.GetProperty("https://payconiq.com/jti").GetString());
        Assert.Equal(header.Path, root.GetProperty("https://payconiq.com/path").GetString());

        var crit = root.GetProperty("crit").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(
        [
            "https://payconiq.com/sub",
            "https://payconiq.com/iss",
            "https://payconiq.com/iat",
            "https://payconiq.com/jti",
            "https://payconiq.com/path",
        ], crit);
    }

    [Fact]
    public void Sign_ThenVerify_WithSameKeyAndBody_Succeeds()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var body = """{"amount":100}"""u8.ToArray();

        var jws = DetachedJwsSigner.Sign(SampleHeader(), body, key);

        Assert.True(DetachedJwsSigner.Verify(jws, body, key));
    }

    [Fact]
    public void Verify_WithTamperedBody_Fails()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var body = """{"amount":100}"""u8.ToArray();
        var tamperedBody = """{"amount":999}"""u8.ToArray();

        var jws = DetachedJwsSigner.Sign(SampleHeader(), body, key);

        Assert.False(DetachedJwsSigner.Verify(jws, tamperedBody, key));
    }

    [Fact]
    public void Verify_WithWrongKey_Fails()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var body = "{}"u8.ToArray();

        var jws = DetachedJwsSigner.Sign(SampleHeader(), body, signingKey);

        Assert.False(DetachedJwsSigner.Verify(jws, body, otherKey));
    }

    [Fact]
    public void Sign_WithEmptyBody_Works()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var jws = DetachedJwsSigner.Sign(SampleHeader(), ReadOnlySpan<byte>.Empty, key);

        Assert.True(DetachedJwsSigner.Verify(jws, ReadOnlySpan<byte>.Empty, key));
    }
}
