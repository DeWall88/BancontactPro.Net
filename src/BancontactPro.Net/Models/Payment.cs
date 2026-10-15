using System.Text.Json.Serialization;

namespace BancontactPro.Models;

/// <summary>
/// A payment resource, returned by <c>GET v3/payments/{id}</c> and as each element of a
/// <see cref="PaymentSearchResponse"/>. Corresponds to the spec's <c>get_payment_response</c> /
/// <c>GetPaymentResponse</c> schema.
/// </summary>
/// <remarks>
/// Deliberately has no <c>TotalAmount</c> property — the spec's <c>required</c> array lists one,
/// but no such property is actually defined anywhere in the schema (a confirmed spec bug); bind
/// only to <see cref="Amount"/>.
/// </remarks>
/// <param name="PaymentId">The payment's id.</param>
/// <param name="CreatedAt">When the payment was created.</param>
/// <param name="ExpireAt">When the payment will expire.</param>
/// <param name="SucceededAt">When the payment succeeded, if it has.</param>
/// <param name="Currency">The payment currency.</param>
/// <param name="Status">The current payment status.</param>
/// <param name="Creditor">The receiving merchant account.</param>
/// <param name="Debtor">
/// The paying customer's details. Both fields are optional here — unlike the webhook callback's
/// debtor, where <c>Iban</c> is required (confirmed from the spec, not an oversight).
/// </param>
/// <param name="Amount">Amount in cents originally requested. <c>0</c> if none was requested.</param>
/// <param name="Description">The merchant's description of the payment, if any.</param>
/// <param name="Message">The debtor's message for the payment, if any.</param>
/// <param name="Reference">The merchant's payment reference, if any.</param>
/// <param name="BulkId">The bulk batch reference, if the profile has bulking enabled.</param>
/// <param name="Links">Hypermedia links.</param>
public sealed record Payment(
    string PaymentId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpireAt,
    DateTimeOffset? SucceededAt,
    string Currency,
    MerchantPaymentStatus Status,
    PaymentCreditor Creditor,
    PaymentDebtor? Debtor,
    long Amount,
    string? Description,
    string? Message,
    string? Reference,
    string? BulkId,
    [property: JsonPropertyName("_links")] PaymentLinks Links);

/// <param name="Name">The debtor's first name, if shared.</param>
/// <param name="Iban">The debtor's masked IBAN, if available yet.</param>
public sealed record PaymentDebtor(string? Name, string? Iban);
