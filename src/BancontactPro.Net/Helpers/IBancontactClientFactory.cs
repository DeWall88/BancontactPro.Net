using BancontactPro.Payments;
using BancontactPro.Reconciliation;
using BancontactPro.Refunds;

namespace BancontactPro.Helpers;

/// <summary>
/// Hands out API clients bound to one <see cref="BancontactProduct"/>: its API key, and for signed
/// calls its PPID. Bancontact issues credentials per product type, so a payment created with the
/// Static QR client must also be fetched, cancelled and refunded with Static QR clients.
/// </summary>
/// <remarks>
/// Asking for a product without a complete API key and PPID pair throws
/// <see cref="BancontactProductNotConfiguredException"/>; asking for a Refund or Reconciliation
/// client without a signing key throws <see cref="BancontactSigningKeyNotConfiguredException"/>.
/// Either way nothing is sent to Bancontact.
/// </remarks>
public interface IBancontactClientFactory
{
    /// <summary>Whether <paramref name="product"/> has a complete API key and PPID pair.</summary>
    bool IsConfigured(BancontactProduct product);

    /// <summary>A Payment API client for <paramref name="product"/>, authenticated with its API key.</summary>
    /// <exception cref="BancontactProductNotConfiguredException">The product has no complete API key and PPID pair.</exception>
    IPaymentClient Payments(BancontactProduct product);

    /// <summary>A Refund API client for <paramref name="product"/>, JWS-signed with its PPID as the <c>sub</c> claim.</summary>
    /// <exception cref="BancontactProductNotConfiguredException">The product has no complete API key and PPID pair.</exception>
    /// <exception cref="BancontactSigningKeyNotConfiguredException">No signing key is configured.</exception>
    IRefundClient Refunds(BancontactProduct product);

    /// <summary>A Reconciliation API client for <paramref name="product"/>, JWS-signed with its PPID as the <c>sub</c> claim.</summary>
    /// <exception cref="BancontactProductNotConfiguredException">The product has no complete API key and PPID pair.</exception>
    /// <exception cref="BancontactSigningKeyNotConfiguredException">No signing key is configured.</exception>
    IReconciliationClient Reconciliation(BancontactProduct product);
}

internal sealed class BancontactClientFactory : IBancontactClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BancontactProOptions _options;

    public BancontactClientFactory(IHttpClientFactory httpClientFactory, Microsoft.Extensions.Options.IOptions<BancontactProOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public bool IsConfigured(BancontactProduct product) => _options.GetCredentials(product) is not null;

    public IPaymentClient Payments(BancontactProduct product)
    {
        EnsureConfigured(product);
        return new PaymentClient(_httpClientFactory.CreateClient(ClientNames.Payments(product)));
    }

    public IRefundClient Refunds(BancontactProduct product)
    {
        EnsureConfigured(product);
        EnsureSigningKey();
        return new RefundClient(_httpClientFactory.CreateClient(ClientNames.Refunds(product)));
    }

    public IReconciliationClient Reconciliation(BancontactProduct product)
    {
        EnsureConfigured(product);
        EnsureSigningKey();
        return new ReconciliationClient(_httpClientFactory.CreateClient(ClientNames.Reconciliation(product)));
    }

    private void EnsureConfigured(BancontactProduct product)
    {
        if (!IsConfigured(product))
        {
            throw new BancontactProductNotConfiguredException(product);
        }
    }

    private void EnsureSigningKey()
    {
        if (!_options.HasSigningKey)
        {
            throw new BancontactSigningKeyNotConfiguredException();
        }
    }
}

/// <summary>The named <see cref="HttpClient"/>s behind each product's clients.</summary>
internal static class ClientNames
{
    public static string Payments(BancontactProduct product) => $"BancontactPro.Payments.{product}";

    public static string Refunds(BancontactProduct product) => $"BancontactPro.Refunds.{product}";

    public static string Reconciliation(BancontactProduct product) => $"BancontactPro.Reconciliation.{product}";
}
