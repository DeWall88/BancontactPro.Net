namespace BancontactPro.Models;

/// <summary>A reconciled refund within a payout (<c>GET v3/reconciliation/refunds</c>).</summary>
/// <param name="RefundId">The refund's id.</param>
/// <param name="PaymentId">The refunded payment's id.</param>
/// <param name="PayoutId">The payout this refund was included in, if known.</param>
/// <param name="PaymentProfileId">The merchant profile id used in the transaction.</param>
/// <param name="MerchantName">The merchant's name.</param>
/// <param name="PaymentChannel">The channel the original payment was made through.</param>
/// <param name="Currency">The transaction currency.</param>
/// <param name="Amount">The total amount in cents.</param>
/// <param name="Reference">The merchant/partner reference, if provided.</param>
/// <param name="Description">The merchant/partner description, if provided.</param>
/// <param name="TransactionDate">When the transaction was created, in UTC.</param>
public sealed record ReconciliationRefundDetails(
    string RefundId,
    string PaymentId,
    string? PayoutId,
    string PaymentProfileId,
    string MerchantName,
    PaymentChannel PaymentChannel,
    string Currency,
    long Amount,
    string? Reference,
    string? Description,
    DateTimeOffset TransactionDate);

/// <param name="Size">The number of elements on this page.</param>
/// <param name="TotalPages">The total number of pages available.</param>
/// <param name="TotalElements">The total number of matching elements.</param>
/// <param name="Number">The current page number.</param>
/// <param name="Refunds">The refunds on this page.</param>
public sealed record PayoutRefundsResponse(
    int Size,
    int TotalPages,
    int TotalElements,
    int Number,
    IReadOnlyList<ReconciliationRefundDetails> Refunds);
