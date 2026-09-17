using System.Text.Json.Serialization;

namespace BancontactPro.Models;

/// <summary>
/// The response to creating a payment (<c>POST v3/payments</c> or <c>POST v3/payments/pos</c>).
/// </summary>
/// <remarks>
/// Note <see cref="ExpiresAt"/> — the spec spells this differently here than on <see cref="Payment"/>
/// (<c>ExpireAt</c>, no "s"). Confirmed from the raw spec, not a typo to normalize away.
/// </remarks>
/// <param name="PaymentId">The created payment's id.</param>
/// <param name="Status">Always <c>PENDING</c> immediately after creation.</param>
/// <param name="CreatedAt">When the payment was created.</param>
/// <param name="ExpiresAt">When the payment will expire.</param>
/// <param name="Amount">Amount in cents requested.</param>
/// <param name="Currency">The payment currency.</param>
/// <param name="Creditor">The receiving merchant account.</param>
/// <param name="Description">The payment description, if any.</param>
/// <param name="Reference">The merchant's payment reference, if any.</param>
/// <param name="Links">Hypermedia links, including the checkout/QR code URLs.</param>
public sealed record CreatePaymentResponse(
    string PaymentId,
    MerchantPaymentStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    long Amount,
    string Currency,
    PaymentCreditor Creditor,
    string? Description,
    string? Reference,
    [property: JsonPropertyName("_links")] PaymentLinks Links);
