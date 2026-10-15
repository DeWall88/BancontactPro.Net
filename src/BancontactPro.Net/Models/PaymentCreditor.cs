namespace BancontactPro.Models;

/// <summary>The creditor (merchant) account receiving a payment.</summary>
/// <param name="ProfileId">The merchant's payment profile configuration id.</param>
/// <param name="MerchantId">The merchant's id.</param>
/// <param name="Name">The merchant's company name, as shown to the debtor.</param>
/// <param name="Iban">The creditor's bank account IBAN.</param>
/// <param name="IdentifyCallbackUrl">The profile's configured identify callback URL.</param>
/// <param name="CallbackUrl">The profile's configured payment callback URL.</param>
public sealed record PaymentCreditor(
    string? ProfileId,
    string? MerchantId,
    string? Name,
    string? Iban,
    string? IdentifyCallbackUrl,
    string? CallbackUrl);
