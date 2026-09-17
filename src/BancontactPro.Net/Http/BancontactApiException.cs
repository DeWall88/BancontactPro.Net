using System.Net;

namespace BancontactPro.Http;

/// <summary>
/// Thrown when Bancontact returns a non-success HTTP response. <see cref="TraceId"/> and
/// <see cref="SpanId"/> are what you'd hand to Bancontact support when opening a ticket, when
/// present — only the Payment API's error responses include them; Refund and Reconciliation
/// don't define those fields at all (confirmed from the raw specs).
/// </summary>
public sealed class BancontactApiException : Exception
{
    /// <summary>The HTTP status code of the response.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>A machine-readable error code, e.g. <c>PAYMENT_NOT_FOUND</c>. Empty if the response body wasn't parseable.</summary>
    public string Code { get; }

    /// <summary>The id of the request/job this error occurred in, for support requests. <c>null</c> outside the Payment API, or if the response body wasn't parseable.</summary>
    public string? TraceId { get; }

    /// <summary>The id of the specific work unit where the error occurred. <c>null</c> outside the Payment API, or if the response body wasn't parseable.</summary>
    public string? SpanId { get; }

    /// <param name="statusCode">The HTTP status code of the response.</param>
    /// <param name="code">A machine-readable error code.</param>
    /// <param name="message">A human-readable description, used as the exception message.</param>
    /// <param name="traceId">The request/job id, for support requests, if present.</param>
    /// <param name="spanId">The work-unit id where the error occurred, if present.</param>
    public BancontactApiException(HttpStatusCode statusCode, string code, string message, string? traceId = null, string? spanId = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        TraceId = traceId;
        SpanId = spanId;
    }
}
