namespace BancontactPro.Models;

/// <summary>
/// Filter body for <c>POST v3/payments/search</c>. All fields are optional (<see cref="From"/>
/// defaults server-side to yesterday). Pagination (<c>page</c>/<c>size</c>) is a separate pair of
/// query parameters on the request, not part of this body.
/// </summary>
/// <param name="From">Defaults to yesterday if omitted.</param>
/// <param name="To">The end of the search window.</param>
/// <param name="PaymentStatuses">Filters results to these statuses, if provided.</param>
/// <param name="Reference">Filters results to this merchant reference, if provided.</param>
public sealed record PaymentSearchQuery(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    IReadOnlyList<MerchantPaymentStatus>? PaymentStatuses = null,
    string? Reference = null);
