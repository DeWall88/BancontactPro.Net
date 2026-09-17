namespace BancontactPro.Models;

/// <summary>
/// Request to create a static QR (in-person/POS) payment (<c>POST v3/payments/pos</c>). If an
/// active payment already exists for the given <see cref="PosId"/>/profile combination, it's
/// invalidated and replaced by the new one.
/// </summary>
/// <param name="Amount">Amount in cents, 1–999,999,999,999.</param>
/// <param name="PosId">The point-of-sale identifier this QR code is displayed at.</param>
/// <param name="Reference">Merchant payment reference, ≤35 chars.</param>
/// <param name="BulkId">Bulk batch reference. Defaults to the profile's if omitted.</param>
/// <param name="Currency">Only <c>EUR</c> is supported.</param>
/// <param name="Description">Shown to the debtor and used in the bank statement.</param>
/// <param name="ShopId">The shop identifier, if applicable.</param>
/// <param name="ShopName">Shown to the user during payment; truncated beyond 23 chars in the remittance info. ≤36 chars.</param>
/// <param name="IdentifyCallbackUrl">HTTPS URL, ≤2048 chars.</param>
/// <param name="CallbackUrl">HTTPS URL, ≤2048 chars.</param>
public sealed record CreateStaticQrPaymentRequest(
    long Amount,
    string PosId,
    string? Reference = null,
    string? BulkId = null,
    string? Currency = null,
    string? Description = null,
    string? ShopId = null,
    string? ShopName = null,
    string? IdentifyCallbackUrl = null,
    string? CallbackUrl = null);
