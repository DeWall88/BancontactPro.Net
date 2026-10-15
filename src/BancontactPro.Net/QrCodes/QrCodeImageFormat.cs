namespace BancontactPro.QrCodes;

/// <summary>The image format for a generated QR code.</summary>
public enum QrCodeImageFormat
{
    /// <summary>PNG raster image. Supports the <c>size</c> parameter.</summary>
    PNG,

    /// <summary>SVG vector image. The <c>size</c> parameter doesn't apply.</summary>
    SVG,
}
