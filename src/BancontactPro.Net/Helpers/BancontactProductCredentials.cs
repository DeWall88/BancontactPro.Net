namespace BancontactPro.Helpers;

/// <summary>
/// The credential pair Bancontact issues for one product type: its API key and its Product
/// Profile ID (PPID). Both come from the same product's entry in Bancontact's onboarding email
/// (pre-production) or the Bancontact Pro portal (production).
/// </summary>
public sealed class BancontactProductCredentials
{
    /// <summary>The bearer API key issued for this product ("A unique API key").</summary>
    public string? ApiKey { get; set; }

    /// <summary>The Product Profile Identifier issued for this product ("A unique Product Profile Identifier or PPID").</summary>
    public string? MerchantProfileId { get; set; }

    /// <summary>Whether both halves of the pair are set.</summary>
    public bool IsComplete => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(MerchantProfileId);

    /// <summary>
    /// Whether neither half is set. An empty entry counts as "not configured", so a deployment can
    /// declare every product's variables and leave the unused ones blank.
    /// </summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(ApiKey) && string.IsNullOrWhiteSpace(MerchantProfileId);
}
