namespace BancontactPro.Models;

/// <summary>
/// The response to creating a refund. <see cref="Status"/> is always <c>PENDING</c> immediately
/// after creation — refund processing is asynchronous; poll <c>GET .../refunds/{refund-id}</c>
/// (see <see cref="RefundModel"/>) for the current status.
/// </summary>
/// <param name="RefundId">The created refund's id.</param>
/// <param name="PaymentId">The payment being refunded.</param>
/// <param name="Status">Always <c>PENDING</c> immediately after creation.</param>
/// <param name="Amount">The refunded amount in cents.</param>
/// <param name="Currency">The refund currency.</param>
/// <param name="Description">The refund description, if any.</param>
/// <param name="CreationDate">When the refund was created.</param>
public sealed record RefundCreationResponse(
    string RefundId,
    string PaymentId,
    RefundStatus Status,
    long Amount,
    string Currency,
    string? Description,
    DateTimeOffset CreationDate);
