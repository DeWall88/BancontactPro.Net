using System.Buffers.Text;
using System.Security.Cryptography;
using BancontactPro.Signing;

namespace BancontactPro.Net.Tests.Signing;

public class JwkConverterTests
{
    [Fact]
    public void RoundTrips_WithJwkExporter()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var json = JwkExporter.ToPublicJwkJson(key, "kid-1");

        using var imported = JwkConverter.ToPublicKey(json)!;

        var expected = key.ExportParameters(includePrivateParameters: false);
        var actual = imported.ExportParameters(includePrivateParameters: false);
        Assert.Equal(Base64Url.EncodeToString(expected.Q.X!), Base64Url.EncodeToString(actual.Q.X!));
        Assert.Equal(Base64Url.EncodeToString(expected.Q.Y!), Base64Url.EncodeToString(actual.Q.Y!));
    }

    [Theory]
    [InlineData("RSA", "P-256")]
    [InlineData("EC", "P-384")]
    public void ToPublicKey_ReturnsNull_ForUnsupportedKeyTypesOrCurves(string kty, string crv)
    {
        var json = $$"""{"kty":"{{kty}}","crv":"{{crv}}","kid":"k","x":"AAAA","y":"AAAA"}""";

        Assert.Null(JwkConverter.ToPublicKey(json));
    }

    [Fact]
    public void ToPublicKey_ReturnsNull_WhenCoordinatesMissing()
    {
        const string json = """{"kty":"EC","crv":"P-256","kid":"k","y":"AAAA"}""";

        Assert.Null(JwkConverter.ToPublicKey(json));
    }
}
