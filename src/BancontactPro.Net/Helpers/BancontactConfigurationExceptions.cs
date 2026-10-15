namespace BancontactPro.Helpers;

/// <summary>
/// Thrown when a client is requested for a <see cref="BancontactProduct"/> that has no complete
/// API key and PPID pair configured. No request is sent to Bancontact: it would be refused anyway.
/// </summary>
public sealed class BancontactProductNotConfiguredException : InvalidOperationException
{
    /// <param name="product">The product that was requested.</param>
    public BancontactProductNotConfiguredException(BancontactProduct product)
        : base($"Bancontact product '{product}' is not configured. Set both BancontactPro:Products:{product}:ApiKey and " +
               $"BancontactPro:Products:{product}:MerchantProfileId (the API key and PPID Bancontact issued for this product).")
    {
        Product = product;
    }

    /// <summary>The product that was requested.</summary>
    public BancontactProduct Product { get; }
}

/// <summary>
/// Thrown when a Refund or Reconciliation client is requested without a signing key. Those APIs
/// accept only a JWS-signed request, verified against a JWKS the merchant hosts.
/// </summary>
public sealed class BancontactSigningKeyNotConfiguredException : InvalidOperationException
{
    /// <summary>Creates the exception with a message that says what to configure.</summary>
    public BancontactSigningKeyNotConfiguredException()
        : base("The Refund and Reconciliation APIs require a signed request. Configure BancontactPro:Kid and " +
               "BancontactPro:PrivateKeyPem, and host the public key as a JWKS whose URL is shared with Bancontact. " +
               "The Payment API works with a product's API key alone.")
    {
    }
}
