namespace BancontactPro.Models;

/// <summary>A settlement batch paid out to the merchant (<c>GET v3/reconciliation/payouts</c>).</summary>
/// <param name="PayoutId">The payout's id.</param>
/// <param name="MerchantId">The merchant id used during aggregation of the payout run.</param>
/// <param name="BulkId">The bulk id used during aggregation, or <c>NONE</c> if not applicable.</param>
/// <param name="Iban">The IBAN the payout was sent to.</param>
/// <param name="PayoutStatus">The payout's status.</param>
/// <param name="PayoutDate">When the payout was created, in UTC.</param>
/// <param name="PayoutCurrency">The payout currency.</param>
/// <param name="TotalPayments">The number of payments included in the payout.</param>
/// <param name="TotalRefunds">The number of refunds included in the payout.</param>
/// <param name="TotalPaymentAmount">The total payment amount in cents contained in the payout.</param>
/// <param name="TotalRefundAmount">The total refund amount in cents contained in the payout.</param>
/// <param name="PayoutAmount">The total amount in cents paid out to the merchant.</param>
public sealed record Payout(
    string PayoutId,
    string MerchantId,
    string BulkId,
    string Iban,
    PayoutStatus PayoutStatus,
    DateTimeOffset PayoutDate,
    string PayoutCurrency,
    int TotalPayments,
    int TotalRefunds,
    long TotalPaymentAmount,
    long TotalRefundAmount,
    long PayoutAmount);

/// <param name="Size">The number of elements on this page.</param>
/// <param name="TotalPages">The total number of pages available.</param>
/// <param name="TotalElements">The total number of matching elements.</param>
/// <param name="Number">The current page number.</param>
/// <param name="Payouts">The payouts on this page. Empty if there were none.</param>
public sealed record PayoutListResponse(
    int Size,
    int TotalPages,
    int TotalElements,
    int Number,
    IReadOnlyList<Payout> Payouts);
