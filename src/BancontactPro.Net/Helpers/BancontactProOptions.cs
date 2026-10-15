namespace BancontactPro.Helpers;

/// <summary>
/// Configuration options for the Bancontact Pro integration, bound from the
/// <c>BancontactPro</c> section of application settings.
/// </summary>
/// <remarks>
/// Bancontact issues an API key and a PPID per product type, so credentials live under
/// <see cref="Products"/>, one complete pair per product you use. A product's API key is all the
/// Payment API needs. The signing key (<see cref="Kid"/> and <see cref="PrivateKeyPem"/>) is
/// shared by all products and required only for the Refund and Reconciliation APIs, which accept
/// nothing but a JWS signature verified against a JWKS the merchant hosts and shares with Bancontact.
/// </remarks>
public sealed class BancontactProOptions
{
    /// <summary>The configuration section key used when binding these options (<c>"BancontactPro"</c>).</summary>
    public const string SectionName = "BancontactPro";

    /// <summary>
    /// The API key and PPID per product type, e.g. <c>BancontactPro:Products:StaticQr:ApiKey</c>.
    /// At least one product must be configured; a product left out (or with both values empty)
    /// is simply not available.
    /// </summary>
    public Dictionary<BancontactProduct, BancontactProductCredentials> Products { get; set; } = [];

    /// <summary>
    /// A PEM-encoded ES256 (P-256) private key (PKCS#8 or SEC1), used to sign Refund and
    /// Reconciliation requests. Optional; set it together with <see cref="Kid"/>.
    /// </summary>
    public string? PrivateKeyPem { get; set; }

    /// <summary>
    /// The key id of the public key in the JWKS this merchant hosts and shares with Bancontact.
    /// Optional; set it together with <see cref="PrivateKeyPem"/>.
    /// </summary>
    public string? Kid { get; set; }

    /// <summary>
    /// The <c>iss</c> claim value for outgoing request signatures. Defaults to <c>"Payconiq"</c>,
    /// which matches the Payment and Refund specs. The Reconciliation spec templates this as
    /// <c>"{Merchant Id}"</c> instead — a genuine, unresolved spec discrepancy (see
    /// docs/research-notes.md) — override this if a call to the Reconciliation API ever needs it.
    /// </summary>
    public string Issuer { get; set; } = "Payconiq";

    /// <summary>Which Bancontact environment to call. Defaults to <see cref="BancontactEnvironment.Preprod"/>.</summary>
    public BancontactEnvironment Environment { get; set; } = BancontactEnvironment.Preprod;

    /// <summary>Whether a signing key (<see cref="Kid"/> and <see cref="PrivateKeyPem"/>) is configured.</summary>
    public bool HasSigningKey => !string.IsNullOrWhiteSpace(Kid) && !string.IsNullOrWhiteSpace(PrivateKeyPem);

    /// <summary>Returns the complete credential pair for <paramref name="product"/>, or <see langword="null"/> when it isn't configured.</summary>
    public BancontactProductCredentials? GetCredentials(BancontactProduct product) =>
        Products.TryGetValue(product, out var credentials) && credentials.IsComplete ? credentials : null;
}
