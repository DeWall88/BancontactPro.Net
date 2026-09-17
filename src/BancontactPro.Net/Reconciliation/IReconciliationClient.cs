using BancontactPro.Models;

namespace BancontactPro.Reconciliation;

/// <summary>
/// The Bancontact Pro Reconciliation API (v3) — accounting reconciliation of bank payouts
/// against transactions. Data is only available D+1 starting at 09:00 CET; don't design any
/// polling/UX around same-day availability.
/// </summary>
public interface IReconciliationClient
{
    /// <summary>
    /// Lists payouts for the merchant (<c>GET v3/reconciliation/payouts</c>).
    /// </summary>
    /// <param name="date">The date to list payouts for. Defaults server-side to the current UTC date.</param>
    /// <param name="page">Zero-based page index. Defaults to 0 server-side.</param>
    /// <param name="size">Page size — the spec fixes this at exactly 10000 (min, max, and default), not a normal range.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    Task<PayoutListResponse> GetPayoutsAsync(DateOnly? date = null, int? page = null, int? size = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists <c>SUCCEEDED</c> payments, filtered by payout or by date range
    /// (<c>GET v3/reconciliation/payments</c>). Either <paramref name="payoutId"/> or both
    /// <paramref name="startDate"/>/<paramref name="endDate"/> should be supplied — the two date
    /// parameters must be supplied together, and the range can span at most 30 days.
    /// </summary>
    /// <param name="payoutId">Filters to payments within this payout.</param>
    /// <param name="startDate">The inclusive start of the date range. Must be supplied together with <paramref name="endDate"/>.</param>
    /// <param name="endDate">The inclusive end of the date range. Must be supplied together with <paramref name="startDate"/>.</param>
    /// <param name="page">Zero-based page index. Defaults to 0 server-side.</param>
    /// <param name="size">Page size — the spec fixes this at exactly 10000 (min, max, and default), not a normal range.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    Task<PayoutPaymentsResponse> GetPaymentsAsync(string? payoutId = null, DateOnly? startDate = null, DateOnly? endDate = null, int? page = null, int? size = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists <c>SUCCEEDED</c> refunds, filtered by payout or by date range
    /// (<c>GET v3/reconciliation/refunds</c>). Same filter rules as <see cref="GetPaymentsAsync"/>.
    /// </summary>
    Task<PayoutRefundsResponse> GetRefundsAsync(string? payoutId = null, DateOnly? startDate = null, DateOnly? endDate = null, int? page = null, int? size = null, CancellationToken cancellationToken = default);
}
