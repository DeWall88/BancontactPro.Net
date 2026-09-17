using System.Security.Cryptography;

namespace BancontactPro.Signing;

/// <summary>
/// Loads an ES256 (P-256) private key from a PEM-encoded string (either PKCS#8 "PRIVATE KEY" or
/// SEC1 "EC PRIVATE KEY" — <c>ECDsa.ImportFromPem</c> handles both transparently).
/// </summary>
public sealed class EcPemRequestSigningKeyProvider : IRequestSigningKeyProvider, IDisposable
{
    /// <inheritdoc />
    public string Kid { get; }

    /// <inheritdoc />
    public ECDsa PrivateKey { get; }

    /// <param name="kid">The key id the merchant registered with Bancontact for its hosted JWKS.</param>
    /// <param name="pem">A PEM-encoded P-256 private key.</param>
    public EcPemRequestSigningKeyProvider(string kid, string pem)
    {
        Kid = kid;
        PrivateKey = ECDsa.Create();
        PrivateKey.ImportFromPem(pem);
    }

    /// <summary>Disposes the underlying <see cref="ECDsa"/> key.</summary>
    public void Dispose() => PrivateKey.Dispose();
}
