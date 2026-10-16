namespace BancontactPro.Helpers;

/// <summary>
/// The Bancontact Pro hosts this library talks to, per <see cref="BancontactEnvironment"/>. Useful for
/// hosts that need the same addresses the clients use, for example to log them or to allow-list them.
/// </summary>
public static class BancontactEndpoints
{
    private static readonly Uri PreprodMerchantApi = new("https://merchant.api.preprod.bancontact.net/");
    private static readonly Uri ProductionMerchantApi = new("https://merchant.api.bancontact.net/");
    private static readonly Uri PreprodJwks = new("https://jwks.preprod.bancontact.net/");
    private static readonly Uri ProductionJwks = new("https://jwks.bancontact.net/");

    /// <summary>The base address of the Payment, Refund and Reconciliation APIs for <paramref name="environment"/>.</summary>
    public static Uri GetMerchantApiBaseUri(BancontactEnvironment environment) => environment switch
    {
        BancontactEnvironment.Production => ProductionMerchantApi,
        _ => PreprodMerchantApi,
    };

    /// <summary>The address of Bancontact's JWKS (the public keys that sign its callbacks) for <paramref name="environment"/>.</summary>
    public static Uri GetJwksUri(BancontactEnvironment environment) => environment switch
    {
        BancontactEnvironment.Production => ProductionJwks,
        _ => PreprodJwks,
    };
}
