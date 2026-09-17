namespace BancontactPro.Models;

/// <summary>A payout's status.</summary>
public enum PayoutStatus
{
    /// <summary>The payout was successfully processed.</summary>
    SUCCEEDED,

    /// <summary>The payout failed.</summary>
    FAILED,
}
