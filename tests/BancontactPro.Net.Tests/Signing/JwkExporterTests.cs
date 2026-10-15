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

    // Windows (CNG) and Linux (OpenSSL) describe P-256 with different friendly names; a check on the name
    // alone rejected valid keys on Linux. Each way a platform can report the curve must be accepted.
    [Theory]
    [InlineData("1.2.840.10045.3.1.7")]
    [InlineData("nistP256")]
    [InlineData("ECDSA_P256")]
    [InlineData("secp256r1")]
    [InlineData("prime256v1")]
    public void IsP256_AcceptsEveryPlatformsNameForTheCurve(string oidValueOrName)
    {
        var oid = oidValueOrName.StartsWith('1') ? new Oid(oidValueOrName, null) : new Oid(null, oidValueOrName);

        Assert.True(JwkExporter.IsP256(ECCurve.CreateFromOid(oid)));
    }

    [Fact]
    public void IsP256_RejectsOtherCurves()
    {
        Assert.False(JwkExporter.IsP256(ECCurve.NamedCurves.nistP384));
        Assert.False(JwkExporter.IsP256(ECCurve.NamedCurves.nistP521));
    }

    [Fact]
    public void ToPublicJwkJson_KeyImportedFromPem_IsAccepted()
    {
        using var generated = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var imported = ECDsa.Create();
        imported.ImportFromPem(generated.ExportPkcs8PrivateKeyPem());

        var json = JwkExporter.ToPublicJwkJson(imported, "my-kid");

        Assert.Contains("\"crv\":\"P-256\"", json);
    }

    [Fact]
    public void ToPublicJwkJson_P384Key_IsRejected()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP384);

        Assert.Throws<NotSupportedException>(() => JwkExporter.ToPublicJwkJson(key, "my-kid"));
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
