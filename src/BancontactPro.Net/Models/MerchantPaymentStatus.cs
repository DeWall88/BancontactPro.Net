namespace BancontactPro.Models;

/// <summary>
/// The single canonical payment status vocabulary, shared identically by the payment resource,
/// the search response, and the webhook callback body (confirmed from the raw OpenAPI specs —
/// there is no separate "webhook event" vocabulary, despite that having seemed plausible before
/// checking).
/// </summary>
public enum MerchantPaymentStatus
{
    /// <summary>Payment created, awaiting the identify step.</summary>
    PENDING,

    /// <summary>The user scanned the QR / opened the app.</summary>
    IDENTIFIED,

    /// <summary>The user confirmed and the bank authorized the payment.</summary>
    AUTHORIZED,

    /// <summary>Bank-side authorization failed. Final.</summary>
    AUTHORIZATION_FAILED,

    /// <summary>Completed. Final.</summary>
    SUCCEEDED,

    /// <summary>Something else went wrong. Final.</summary>
    FAILED,

    /// <summary>Cancelled by the user or merchant. Final.</summary>
    CANCELLED,

    /// <summary>Not completed in time. Final.</summary>
    EXPIRED,

    /// <summary>Awaiting merchant acknowledgement — VOID-mode flow only.</summary>
    PENDING_MERCHANT_ACKNOWLEDGEMENT,

    /// <summary>Cancelled after consumer confirmation — VOID-mode flow only. Final.</summary>
    VOIDED,
}
