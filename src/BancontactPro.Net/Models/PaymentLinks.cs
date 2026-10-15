namespace BancontactPro.Models;

/// <summary>
/// Hypermedia links for a payment. <see cref="Self"/>, <see cref="Deeplink"/>, and
/// <see cref="Qrcode"/> are always present; <see cref="Cancel"/>, <see cref="Refund"/>, and
/// <see cref="Checkout"/> depend on the payment's current status.
/// </summary>
/// <param name="Self">The payment resource URL — for polling, not a QR code.</param>
/// <param name="Deeplink">A mobile deep link (opens the banking app directly).</param>
/// <param name="Qrcode">The QR code image URL.</param>
/// <param name="Cancel">Present only while the payment is cancellable.</param>
/// <param name="Refund">Present only once the payment has succeeded.</param>
/// <param name="Checkout">The hosted checkout page URL, present on creation.</param>
public sealed record PaymentLinks(
    PaymentLink Self,
    PaymentLink Deeplink,
    PaymentLink Qrcode,
    PaymentLink? Cancel,
    PaymentLink? Refund,
    PaymentLink? Checkout);

/// <param name="Href">The link target URL.</param>
public sealed record PaymentLink(string Href);
