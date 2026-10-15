namespace BancontactPro.QrCodes;

/// <summary>
/// Builds the client-templated QR/deeplink URLs used by the On a Receipt, Top Up, and Static QR
/// products. Unlike On a Display (<c>POST /v3/payments</c>) or Static QR's per-transaction amount
/// (<c>POST /v3/payments/pos</c>), these URLs are never returned by an API call — the merchant
/// constructs them locally by templating a known URL scheme, confirmed against the
/// <see href="https://docs.bancontactpro.com/guides/general/payloadurlupdate">Payment link update</see>
/// migration page and the per-product guides.
/// </summary>
public static class PaymentLinkBuilder
{
    private const string PayHost = "https://pay.bancontact.net";
    private const string QrCodeGeneratorHost = "https://qrcodegenerator.api.bancontact.net";

    /// <summary>
    /// Builds the payload URL for a fixed/open/modifiable-amount QR or deeplink — On a Receipt,
    /// Top Up, or (were it not discontinued) Invoice. All parameters are optional: omit
    /// <paramref name="amountCents"/> for an open-value QR the payer fills in themselves.
    /// </summary>
    /// <param name="productProfileId">The Product Profile ID (PPID) this payment belongs to.</param>
    /// <param name="amountCents">
    /// The amount in euro cents, 1–999,999 (a much smaller cap than the Payment API's own
    /// 999,999,999,999 — this is a constraint of the QR payload scheme itself, not a typo).
    /// </param>
    /// <param name="description">A description shown to the payer, ≤35 chars.</param>
    /// <param name="reference">A merchant reference, ≤35 chars.</param>
    public static string BuildFixedAmountPaymentUrl(string productProfileId, long? amountCents = null, string? description = null, string? reference = null)
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

        var url = $"{PayHost}/t/1/{Uri.EscapeDataString(productProfileId)}";
        return query.Count > 0 ? $"{url}?{string.Join('&', query)}" : url;
    }

    /// <summary>
    /// Builds the payload URL for a Static QR product's printed code — a fixed "location" link
    /// with no amount. This is printed once; the per-transaction amount is attached separately
    /// via <c>IPaymentClient.CreateStaticQrPaymentAsync</c>, not this URL.
    /// </summary>
    /// <param name="productProfileId">The Product Profile ID (PPID) this POS belongs to.</param>
    /// <param name="posId">The point-of-sale identifier this QR code is printed at.</param>
    public static string BuildStaticQrLocationUrl(string productProfileId, string posId) =>
        $"{PayHost}/l/1/{Uri.EscapeDataString(productProfileId)}/{Uri.EscapeDataString(posId)}";

    /// <summary>
    /// Wraps a payload URL (from <see cref="BuildFixedAmountPaymentUrl"/> or
    /// <see cref="BuildStaticQrLocationUrl"/>) into a renderable QR code image URL.
    /// </summary>
    /// <param name="payloadUrl">The payload URL to encode into the QR code.</param>
    /// <param name="format">The image format. Defaults to PNG.</param>
    /// <param name="size">
    /// The image size. Only meaningful for PNG (ignored — and normally omitted — for SVG). Omit
    /// to use the service's own default (small).
    /// </param>
    public static string BuildQrCodeImageUrl(string payloadUrl, QrCodeImageFormat format = QrCodeImageFormat.PNG, QrCodeImageSize? size = null)
    {
        var query = new List<string> { $"f={format}" };
        if (format == QrCodeImageFormat.PNG && size is not null)
        {
            query.Add($"s={size}");
        }

        query.Add($"c={Uri.EscapeDataString(payloadUrl)}");
        return $"{QrCodeGeneratorHost}/qrcode?{string.Join('&', query)}";
    }
}
