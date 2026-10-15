namespace BancontactPro.Models;

/// <summary>
/// Request to create an online payment (<c>POST v3/payments</c>). Only <see cref="Amount"/> is
/// required — <see cref="IdentifyCallbackUrl"/>, <see cref="CallbackUrl"/>, and
/// <see cref="ReturnUrl"/> all fall back to profile-level defaults when omitted.
/// </summary>
/// <param name="Amount">Amount in cents, 1–999,999,999,999.</param>
/// <param name="Reference">Merchant payment reference, ≤35 chars (SEPA character set).</param>
/// <param name="BulkId">Bulk batch reference, ≤35 chars. Defaults to the profile's if omitted.</param>
/// <param name="Currency">Only <c>EUR</c> is supported.</param>
/// <param name="Description">Shown to the debtor and used in the bank statement, ≤140 chars.</param>
/// <param name="IdentifyCallbackUrl">HTTPS URL, ≤2048 chars.</param>
/// <param name="CallbackUrl">HTTPS URL, ≤2048 chars.</param>
/// <param name="ReturnUrl">HTTPS URL, ≤2048 chars.</param>
public sealed record CreatePaymentRequest(
    long Amount,
    string? Reference = null,
    string? BulkId = null,
    string? Currency = null,
    string? Description = null,
    string? IdentifyCallbackUrl = null,
    string? CallbackUrl = null,
    string? ReturnUrl = null);
