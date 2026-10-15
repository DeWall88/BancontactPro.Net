namespace BancontactPro.Models;

/// <summary>
/// Response to <c>GET v3/payments/{id}/debtor/refundIban</c> — the debtor's unmasked IBAN, for
/// the merchant to issue a refund transfer directly. Corresponds to the spec's oddly-named
/// <c>refund-response</c> schema (it isn't related to the Refund API's own response shapes).
/// </summary>
/// <param name="Iban">The debtor's unmasked IBAN.</param>
public sealed record DebtorRefundIban(string Iban);
