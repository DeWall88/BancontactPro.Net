using System.Security.Cryptography;

namespace BancontactPro.Signing;

/// <summary>
/// Supplies the merchant's ES256 (P-256) private key and its registered key id, used to sign
/// outgoing requests. See <see cref="EcPemRequestSigningKeyProvider"/> for the default,
/// PEM-backed implementation.
/// </summary>
public interface IRequestSigningKeyProvider
{
    /// <summary>The key id the merchant registered with Bancontact for its hosted JWKS.</summary>
    string Kid { get; }

    /// <summary>The merchant's ES256 private key, used to sign the detached JWS.</summary>
    ECDsa PrivateKey { get; }
}
