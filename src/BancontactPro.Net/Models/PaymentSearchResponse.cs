namespace BancontactPro.Models;

/// <summary>The paginated response to <c>POST v3/payments/search</c>.</summary>
/// <param name="Size">The number of elements on this page.</param>
/// <param name="TotalPages">The total number of pages available.</param>
/// <param name="TotalElements">The total number of matching elements.</param>
/// <param name="Number">The current page number.</param>
/// <param name="Details">The matching payments on this page.</param>
public sealed record PaymentSearchResponse(
    int Size,
    int TotalPages,
    int TotalElements,
    int Number,
    IReadOnlyList<Payment> Details);
