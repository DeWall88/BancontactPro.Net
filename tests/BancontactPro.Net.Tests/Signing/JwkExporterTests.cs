using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using BancontactPro.Signing;

namespace BancontactPro.Net.Tests.Signing;

public class JwkExporterTests
{
    [Fact]
    public void ToPublicJwkJson_ContainsExpectedFieldsAndCorrectCoordinates()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(includePrivateParameters: false);

        var json = JwkExporter.ToPublicJwkJson(key, "my-kid");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("EC", root.GetProperty("kty").GetString());
        Assert.Equal("P-256", root.GetProperty("crv").GetString());
        Assert.Equal("my-kid", root.GetProperty("kid").GetString());
        Assert.Equal("sig", root.GetProperty("use").GetString());
        Assert.Equal("ES256", root.GetProperty("alg").GetString());
        Assert.Equal(Base64Url.EncodeToString(parameters.Q.X!), root.GetProperty("x").GetString());
        Assert.Equal(Base64Url.EncodeToString(parameters.Q.Y!), root.GetProperty("y").GetString());
    }

    [Fact]
    public void ToPublicJwkJson_DoesNotLeakPrivateKey()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var json = JwkExporter.ToPublicJwkJson(key, "my-kid");

        Assert.DoesNotContain("\"d\"", json);
    }

    [Fact]
    public void ToPublicJwkJson_RejectsNonP256Curve()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP384);

        Assert.Throws<NotSupportedException>(() => JwkExporter.ToPublicJwkJson(key, "my-kid"));
    }
}
