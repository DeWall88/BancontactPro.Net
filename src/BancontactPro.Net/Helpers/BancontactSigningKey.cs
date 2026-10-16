namespace BancontactPro.Helpers;

/// <summary>
/// The merchant's own ES256 (P-256) signing key, used to sign Refund and Reconciliation requests.
/// </summary>
/// <param name="Kid">The key id of the public key in the JWKS this merchant hosts and shares with Bancontact.</param>
/// <param name="PrivateKeyPem">A PEM-encoded P-256 private key (PKCS#8 or SEC1).</param>
public sealed record BancontactSigningKey(string Kid, string PrivateKeyPem)
{
    /// <summary>Shows the key id only: the private key must never end up in a log.</summary>
    public override string ToString() => $"BancontactSigningKey {{ Kid = {Kid}, PrivateKeyPem = *** }}";
}
