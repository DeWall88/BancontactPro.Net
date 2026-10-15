namespace BancontactPro.Models;

/// <summary>A refund's current details (<c>GET v3/payments/{payment-id}/refunds/{refund-id}</c>).</summary>
/// <param name="RefundId">The refund's id.</param>
/// <param name="PaymentId">The payment being refunded.</param>
/// <param name="Amount">The refunded amount in cents.</param>
/// <param name="Currency">The refund currency.</param>
/// <param name="Status">The refund's current status.</param>
/// <param name="Description">The refund description, if any.</param>
/// <param name="CreationDate">When the refund was created.</param>
public sealed record RefundModel(
    string RefundId,
    string PaymentId,
    long Amount,
    string Currency,
    RefundStatus Status,
    string? Description,
    DateTimeOffset CreationDate);
