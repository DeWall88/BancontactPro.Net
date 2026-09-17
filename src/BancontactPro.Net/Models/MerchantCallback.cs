namespace BancontactPro.Models;

/// <summary>
/// The body Bancontact posts to the merchant's callback URL, per the confirmed
/// <c>merchant-callback</c> schema. Deliberately has no <c>TotalAmount</c> property — the spec's
/// <c>required</c> array lists one, but no such property is actually defined anywhere in the
/// schema (a confirmed spec bug); bind only to <see cref="Amount"/>.
/// </summary>
/// <param name="PaymentId">The payment this callback reports on.</param>
/// <param name="Currency">The payment currency. Only <c>EUR</c> is defined by the spec today.</param>
/// <param name="Amount">The payment amount, in cents.</param>
/// <param name="Description">The merchant-supplied description, if any.</param>
/// <param name="Reference">The merchant-supplied reference, if any.</param>
/// <param name="CreatedAt">When the payment was created.</param>
/// <param name="ExpireAt">When the payment expires, if applicable.</param>
/// <param name="SucceededAt">When the payment succeeded, if it has.</param>
/// <param name="Status">The current payment status.</param>
/// <param name="Debtor">The paying customer's (masked) bank details.</param>
public sealed record MerchantCallback(
    string PaymentId,
    string Currency,
    long Amount,
    string? Description,
    string? Reference,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpireAt,
    DateTimeOffset? SucceededAt,
    MerchantPaymentStatus Status,
    CallbackDebtor Debtor);

/// <param name="Iban">The paying customer's masked IBAN.</param>
/// <param name="Name">The paying customer's name, if shared.</param>
public sealed record CallbackDebtor(string Iban, string? Name);
