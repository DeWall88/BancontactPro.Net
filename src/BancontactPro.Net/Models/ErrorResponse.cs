namespace BancontactPro.Models;

/// <summary>
/// The error response shape shared identically by the Payment, Refund, and Reconciliation specs.
/// </summary>
/// <param name="Code">A machine-readable error code, e.g. <c>PAYMENT_NOT_FOUND</c>.</param>
/// <param name="Message">A human-readable description.</param>
/// <param name="TraceId">The id of the request/job this error occurred in — hand this to Bancontact support.</param>
/// <param name="SpanId">The id of the specific work unit where the error occurred.</param>
public sealed record ErrorResponse(string Code, string Message, string TraceId, string SpanId);
