namespace BancontactPro.Helpers;

/// <summary>
/// Configuration options for the Bancontact Pro integration, bound from the
/// <c>BancontactPro</c> section of application settings.
/// </summary>
public sealed class BancontactProOptions
{
    /// <summary>The configuration section key used when binding these options (<c>"BancontactPro"</c>).</summary>
    public const string SectionName = "BancontactPro";

    /// <summary>The bearer API key issued for the merchant profile being called.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The Product Profile ID (PPID) issued for this integration during onboarding.</summary>
    public string? MerchantProfileId { get; set; }

    /// <summary>A PEM-encoded ES256 (P-256) private key (PKCS#8 or SEC1), used to sign outgoing requests.</summary>
    public string? PrivateKeyPem { get; set; }

    /// <summary>The key id registered with Bancontact for this merchant's hosted JWKS.</summary>
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
}
