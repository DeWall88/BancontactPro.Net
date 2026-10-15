namespace BancontactPro.Signing;

/// <summary>
/// Per-merchant configuration used to sign outgoing requests to Bancontact Pro.
/// </summary>
public sealed class BancontactSigningOptions
{
    /// <summary>The bearer API key issued for the merchant profile being called.</summary>
    public required string ApiKey { get; set; }

    /// <summary>The merchant profile id (<c>sub</c> claim).</summary>
    public required string MerchantProfileId { get; set; }

    /// <summary>
    /// The <c>iss</c> claim value. The Payment and Refund specs hardcode this to
    /// <c>"Payconiq"</c>; the Reconciliation spec templates it as <c>"{Merchant Id}"</c> instead
    /// (see docs/research-notes.md — genuinely unresolved against the spec alone). Defaults to
    /// <c>"Payconiq"</c>; override per API family if Bancontact devsupport confirms otherwise.
    /// </summary>
    public string Issuer { get; set; } = "Payconiq";
}
