using BancontactPro.Models;

namespace BancontactPro.Refunds;

/// <summary>The Bancontact Pro Refund API (v3).</summary>
public interface IRefundClient
{
    /// <summary>
    /// Creates a refund for a payment (<c>POST v3/payments/{payment-id}/refunds</c>).
    /// </summary>
    /// <param name="paymentId">The payment to refund.</param>
    /// <param name="request">The refund details. Partial refunds are just "specify any amount".</param>
    /// <param name="idempotencyKey">
    /// A caller-generated key, ≤64 chars, unique per distinct refund attempt. Reusing a key with
    /// different <paramref name="request"/> parameters throws <see cref="RefundIdempotencyConflictException"/>.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    Task<RefundCreationResponse> CreateAsync(string paymentId, CreateRefundRequest request, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Gets a refund's current status (<c>GET v3/payments/{payment-id}/refunds/{refund-id}</c>).</summary>
    Task<RefundModel> GetAsync(string paymentId, string refundId, CancellationToken cancellationToken = default);
}
