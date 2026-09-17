namespace BancontactPro.Signing;

/// <summary>Configuration for fetching Bancontact's JWKS.</summary>
public sealed class JwksClientOptions
{
    /// <summary>
    /// The JWKS endpoint to fetch from — <c>https://jwks.bancontact.net</c> (PROD) or
    /// <c>https://jwks.preprod.bancontact.net</c> (PREPROD).
    /// </summary>
    public required Uri JwksUri { get; set; }
}
