namespace BancontactPro.Models;

/// <summary>
/// Request to create a refund (<c>POST v3/payments/{payment-id}/refunds</c>). <see cref="Amount"/>
/// is required — partial refunds are simply "specify any amount," there's no separate full/partial
/// toggle. No client-side time-window check exists either; if Bancontact enforces one, it
/// surfaces as a runtime <c>422</c> (<c>REFUND_NOT_POSSIBLE</c>/<c>REFUND_NOT_ALLOWED</c>).
/// </summary>
/// <param name="Amount">Amount in cents, 1–999,999,999,999 — same bounds as a payment amount.</param>
/// <param name="Currency">Only <c>EUR</c> is supported.</param>
/// <param name="Description">An optional refund description.</param>
public sealed record CreateRefundRequest(long Amount, string Currency, string? Description = null);
