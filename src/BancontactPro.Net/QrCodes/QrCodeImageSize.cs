namespace BancontactPro.QrCodes;

/// <summary>
/// The generated QR code's pixel size. Only applies to <see cref="QrCodeImageFormat.PNG"/> —
/// ignored for SVG, which is resolution-independent.
/// </summary>
public enum QrCodeImageSize
{
    /// <summary>180x180.</summary>
    S,

    /// <summary>250x250.</summary>
    M,

    /// <summary>400x400.</summary>
    L,

    /// <summary>800x800.</summary>
    XL,
}
