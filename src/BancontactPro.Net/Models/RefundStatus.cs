namespace BancontactPro.Models;

/// <summary>
/// A refund's status. Note <c>PROCESSING</c> and the error code <c>DEBTOR_IBAN_NOT_AVAILABLE</c>
/// were both removed in Refund API v3.0.2 per the spec's changelog — don't model either.
/// </summary>
public enum RefundStatus
{
    /// <summary>Accepted by Bancontact and will be processed soon.</summary>
    PENDING,

    /// <summary>Processed successfully; money moved from merchant to debtor.</summary>
    REFUNDED,

    /// <summary>Processing failed for technical reasons; no money moved.</summary>
    FAILED,
}
