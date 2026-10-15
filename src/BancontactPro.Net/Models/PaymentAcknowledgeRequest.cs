namespace BancontactPro.Models;

/// <summary>
/// Request body for <c>POST v3/payments/{id}/acknowledge</c> — the merchant confirming receipt of
/// a payment status callback, for the VOID-mode flow (<see cref="MerchantPaymentStatus.PENDING_MERCHANT_ACKNOWLEDGEMENT"/>).
/// </summary>
/// <param name="Currency">The payment currency.</param>
/// <param name="Amount">Amount in cents.</param>
/// <param name="Reference">The merchant's payment reference, if any.</param>
public sealed record PaymentAcknowledgeRequest(string Currency, long Amount, string? Reference = null);
