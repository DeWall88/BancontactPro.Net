using System.Security.Cryptography;
using BancontactPro.Signing;

namespace BancontactPro.Net.Tests.Signing;

public class EcPemRequestSigningKeyProviderTests
{
    [Fact]
    public void LoadsPkcs8Pem()
    {
        using var generated = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = generated.ExportPkcs8PrivateKeyPem();

        using var provider = new EcPemRequestSigningKeyProvider("my-kid", pem);

        Assert.Equal("my-kid", provider.Kid);
        Assert.Equal(
            generated.ExportParameters(includePrivateParameters: true).D,
            provider.PrivateKey.ExportParameters(includePrivateParameters: true).D);
    }

    [Fact]
    public void LoadsSec1Pem()
    {
        using var generated = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = generated.ExportECPrivateKeyPem();

        using var provider = new EcPemRequestSigningKeyProvider("my-kid", pem);

        Assert.Equal(
            generated.ExportParameters(includePrivateParameters: true).D,
            provider.PrivateKey.ExportParameters(includePrivateParameters: true).D);
    }

    [Fact]
    public void LoadedKeyCanSignAndBeVerifiedAgainstOriginal()
    {
        using var generated = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = generated.ExportPkcs8PrivateKeyPem();
        using var provider = new EcPemRequestSigningKeyProvider("my-kid", pem);

        var data = "hello"u8.ToArray();
        var signature = provider.PrivateKey.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        Assert.True(generated.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }
}
