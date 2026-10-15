namespace BancontactPro.Helpers;

/// <summary>
/// The Bancontact Pro product types. Bancontact issues a separate API key and Product Profile ID
/// (PPID) per product type, so every call is made on behalf of exactly one of these.
/// </summary>
public enum BancontactProduct
{
    /// <summary><see href="https://docs.bancontactpro.com/guides/instore/ondisplay052025v4">On a Display</see>: a QR code shown on a screen at the till.</summary>
    OnDisplay,

    /// <summary><see href="https://docs.bancontactpro.com/guides/instore/receipt052025v4">On a Receipt</see>: a QR code printed on the receipt.</summary>
    OnReceipt,

    /// <summary><see href="https://docs.bancontactpro.com/guides/instore/staticqr052025v4">Static QR</see>: a printed, reusable QR code at a point of sale.</summary>
    StaticQr,

    /// <summary><see href="https://docs.bancontactpro.com/guides/online/topup052025v4">Top Up</see>: topping up a balance.</summary>
    TopUp,

    /// <summary><see href="https://docs.bancontactpro.com/guides/invoice/invoice052025v4">Invoice</see>: a QR code on an invoice.</summary>
    Invoice,
}
