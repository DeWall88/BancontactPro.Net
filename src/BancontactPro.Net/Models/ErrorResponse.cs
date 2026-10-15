namespace BancontactPro.Models;

/// <summary>
/// The error response shape used by all three specs. <see cref="Code"/>/<see cref="Message"/> are
/// common to all of them; <see cref="TraceId"/>/<see cref="SpanId"/> are only ever present on the
/// Payment API's error responses — the Refund and Reconciliation specs' <c>ErrorResponse</c>
/// schema doesn't define them at all (confirmed from the raw specs; not an omission to fix).
/// </summary>
/// <param name="Code">A machine-readable error code, e.g. <c>PAYMENT_NOT_FOUND</c>.</param>
/// <param name="Message">A human-readable description.</param>
/// <param name="TraceId">The id of the request/job this error occurred in — hand this to Bancontact support. Payment API only.</param>
/// <param name="SpanId">The id of the specific work unit where the error occurred. Payment API only.</param>
public sealed record ErrorResponse(string Code, string Message, string? TraceId = null, string? SpanId = null);
