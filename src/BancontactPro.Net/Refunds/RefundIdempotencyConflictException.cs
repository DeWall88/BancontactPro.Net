using BancontactPro.Http;

namespace BancontactPro.Refunds;

/// <summary>
/// Thrown when the Refund API reports <c>REFUND_REQUEST_CONFLICT</c> — the given
/// <see cref="IdempotencyKey"/> was already used to create a refund with different parameters.
/// Surfaced as a distinct type because this is a caller bug (reusing a key for a different
/// refund), not a transient failure worth retrying as-is.
/// </summary>
public sealed class RefundIdempotencyConflictException : Exception
{
    /// <summary>The <c>Idempotency-Key</c> that was reused with different parameters.</summary>
    public string IdempotencyKey { get; }

    /// <param name="idempotencyKey">The reused key.</param>
    /// <param name="innerException">The underlying <see cref="BancontactApiException"/>.</param>
    public RefundIdempotencyConflictException(string idempotencyKey, BancontactApiException innerException)
        : base($"The Idempotency-Key '{idempotencyKey}' was already used to create a refund with different parameters.", innerException)
    {
        IdempotencyKey = idempotencyKey;
    }
}
