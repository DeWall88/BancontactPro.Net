using BancontactPro.Helpers;

namespace BancontactPro.QrCodes;

/// <summary>
/// Builds the client-templated QR/deeplink URLs used by the On a Receipt, Top Up, and Static QR
/// products. Unlike On a Display (<c>POST /v3/payments</c>) or Static QR's per-transaction amount
/// (<c>POST /v3/payments/pos</c>), these URLs are never returned by an API call — the merchant
/// constructs them locally by templating a known URL scheme, confirmed against the
/// <see href="https://docs.bancontactpro.com/guides/general/payloadurlupdate">Payment link update</see>
/// migration page and the per-product guides.
/// </summary>
/// <remarks>
/// Each method takes the <see cref="BancontactEnvironment"/> to build for; pass the configured
/// <see cref="BancontactProOptions.Environment"/>. Production uses <c>pay.bancontact.net</c> and
/// <c>qrcodegenerator.api.bancontact.net</c>. Pre-production uses <c>pay.acc.bancontact.net</c> and
/// <c>qrcodegenerator.api.preprod.bancontact.net</c>, the hosts in the links Bancontact's own
/// pre-production API returns. The production generator rejects a pre-production payload, so a
/// payload and its image URL must be built for the same environment.
/// </remarks>
public static class PaymentLinkBuilder
{
    /// <summary>
    /// Builds the payload URL for a fixed/open/modifiable-amount QR or deeplink — On a Receipt,
    /// Top Up, or (were it not discontinued) Invoice. All parameters are optional: omit
    /// <paramref name="amountCents"/> for an open-value QR the payer fills in themselves.
    /// </summary>
    /// <param name="environment">The Bancontact environment the payment belongs to.</param>
    /// <param name="productProfileId">The Product Profile ID (PPID) this payment belongs to.</param>
    /// <param name="amountCents">
    /// The amount in euro cents, 1–999,999 (a much smaller cap than the Payment API's own
    /// 999,999,999,999 — this is a constraint of the QR payload scheme itself, not a typo).
    /// </param>
    /// <param name="description">A description shown to the payer, ≤35 chars.</param>
    /// <param name="reference">A merchant reference, ≤35 chars.</param>
    public static string BuildFixedAmountPaymentUrl(BancontactEnvironment environment, string productProfileId, long? amountCents = null, string? description = null, string? reference = null)
    {
        var query = new List<string>();
        if (description is not null)
        {
            query.Add($"D={Uri.EscapeDataString(description)}");
        }

        if (amountCents is not null)
        {
            query.Add($"A={amountCents}");
        }

        if (reference is not null)
        {
            query.Add($"R={Uri.EscapeDataString(reference)}");
        }

        var url = $"{GetPayHost(environment)}/t/1/{Uri.EscapeDataString(productProfileId)}";
        return query.Count > 0 ? $"{url}?{string.Join('&', query)}" : url;
    }

    /// <summary>
    /// Builds the payload URL for a Static QR product's printed code — a fixed "location" link
    /// with no amount. This is printed once; the per-transaction amount is attached separately
    /// via <c>IPaymentClient.CreateStaticQrPaymentAsync</c>, not this URL.
    /// </summary>
    /// <param name="environment">The Bancontact environment the POS belongs to.</param>
    /// <param name="productProfileId">The Product Profile ID (PPID) this POS belongs to.</param>
    /// <param name="posId">The point-of-sale identifier this QR code is printed at.</param>
    public static string BuildStaticQrLocationUrl(BancontactEnvironment environment, string productProfileId, string posId) =>
        $"{GetPayHost(environment)}/l/1/{Uri.EscapeDataString(productProfileId)}/{Uri.EscapeDataString(posId)}";

    /// <summary>
    /// Wraps a payload URL (from <see cref="BuildFixedAmountPaymentUrl"/> or
    /// <see cref="BuildStaticQrLocationUrl"/>) into a renderable QR code image URL.
    /// </summary>
    /// <param name="environment">
    /// The Bancontact environment whose generator renders the image: the one the payload was built for.
    /// </param>
    /// <param name="payloadUrl">The payload URL to encode into the QR code.</param>
    /// <param name="format">The image format. Defaults to PNG.</param>
    /// <param name="size">
    /// The image size. Only meaningful for PNG (ignored — and normally omitted — for SVG). Omit
    /// to use the service's own default (small).
    /// </param>
    public static string BuildQrCodeImageUrl(BancontactEnvironment environment, string payloadUrl, QrCodeImageFormat format = QrCodeImageFormat.PNG, QrCodeImageSize? size = null)
    {
        var query = new List<string> { $"f={format}" };
        if (format == QrCodeImageFormat.PNG && size is not null)
        {
            query.Add($"s={size}");
        }

        query.Add($"c={Uri.EscapeDataString(payloadUrl)}");
        return $"{GetQrCodeGeneratorHost(environment)}/qrcode?{string.Join('&', query)}";
    }

    private static string GetPayHost(BancontactEnvironment environment) => environment switch
    {
        BancontactEnvironment.Production => "https://pay.bancontact.net",
        _ => "https://pay.acc.bancontact.net",
    };

    private static string GetQrCodeGeneratorHost(BancontactEnvironment environment) => environment switch
    {
        BancontactEnvironment.Production => "https://qrcodegenerator.api.bancontact.net",
        _ => "https://qrcodegenerator.api.preprod.bancontact.net",
    };
}
