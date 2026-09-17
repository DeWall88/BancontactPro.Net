using BancontactPro.QrCodes;

namespace BancontactPro.Net.Tests.QrCodes;

public class PaymentLinkBuilderTests
{
    // Worked examples taken verbatim from the "Payment link update" migration page and the
    // On a Receipt / Static QR guides, so these assertions are regression-proof against the
    // exact strings Bancontact itself publishes.

    [Fact]
    public void BuildFixedAmountPaymentUrl_WithAllFields_MatchesPublishedExample()
    {
        var url = PaymentLinkBuilder.BuildFixedAmountPaymentUrl(
            "5c18cbd1296e9a26d3278518", amountCents: 1000, description: "Receipt Payment", reference: "sd89sd91?sd9");

        Assert.Equal(
            "https://pay.bancontact.net/t/1/5c18cbd1296e9a26d3278518?D=Receipt%20Payment&A=1000&R=sd89sd91%3Fsd9",
            url);
    }

    [Fact]
    public void BuildFixedAmountPaymentUrl_WithNoOptionalFields_ReturnsBareUrl()
    {
        var url = PaymentLinkBuilder.BuildFixedAmountPaymentUrl("5c18cbd1296e9a26d3278518");

        Assert.Equal("https://pay.bancontact.net/t/1/5c18cbd1296e9a26d3278518", url);
    }

    [Fact]
    public void BuildFixedAmountPaymentUrl_WithoutAmount_OmitsAParam()
    {
        var url = PaymentLinkBuilder.BuildFixedAmountPaymentUrl("ppid", description: "Open value");

        Assert.Equal("https://pay.bancontact.net/t/1/ppid?D=Open%20value", url);
    }

    [Fact]
    public void BuildStaticQrLocationUrl_MatchesPublishedExample()
    {
        var url = PaymentLinkBuilder.BuildStaticQrLocationUrl("5bb37284e35e2b29e363df22", "POS00001");

        Assert.Equal("https://pay.bancontact.net/l/1/5bb37284e35e2b29e363df22/POS00001", url);
    }

    [Fact]
    public void BuildQrCodeImageUrl_ForFixedAmountPayload_MatchesPublishedExample()
    {
        var payload = PaymentLinkBuilder.BuildFixedAmountPaymentUrl(
            "5c18cbd1296e9a26d3278518", amountCents: 1000, description: "Receipt Payment", reference: "sd89sd91?sd9");

        var qrUrl = PaymentLinkBuilder.BuildQrCodeImageUrl(payload, QrCodeImageFormat.PNG, QrCodeImageSize.XL);

        Assert.Equal(
            "https://qrcodegenerator.api.bancontact.net/qrcode?f=PNG&s=XL&c=https%3A%2F%2Fpay.bancontact.net%2Ft%2F1%2F5c18cbd1296e9a26d3278518%3FD%3DReceipt%2520Payment%26A%3D1000%26R%3Dsd89sd91%253Fsd9",
            qrUrl);
    }

    [Fact]
    public void BuildQrCodeImageUrl_ForStaticQrPayload_MatchesPublishedExample()
    {
        var payload = PaymentLinkBuilder.BuildStaticQrLocationUrl("5bb37284e35e2b29e363df22", "POS00001");

        var qrUrl = PaymentLinkBuilder.BuildQrCodeImageUrl(payload, QrCodeImageFormat.PNG, QrCodeImageSize.XL);

        Assert.Equal(
            "https://qrcodegenerator.api.bancontact.net/qrcode?f=PNG&s=XL&c=https%3A%2F%2Fpay.bancontact.net%2Fl%2F1%2F5bb37284e35e2b29e363df22%2FPOS00001",
            qrUrl);
    }

    [Fact]
    public void BuildQrCodeImageUrl_Svg_OmitsSizeEvenIfProvided()
    {
        var qrUrl = PaymentLinkBuilder.BuildQrCodeImageUrl("https://pay.bancontact.net/l/1/ppid/pos", QrCodeImageFormat.SVG, QrCodeImageSize.L);

        Assert.Equal("https://qrcodegenerator.api.bancontact.net/qrcode?f=SVG&c=https%3A%2F%2Fpay.bancontact.net%2Fl%2F1%2Fppid%2Fpos", qrUrl);
    }

    [Fact]
    public void BuildQrCodeImageUrl_NoSizeSpecified_OmitsSParam()
    {
        var qrUrl = PaymentLinkBuilder.BuildQrCodeImageUrl("https://pay.bancontact.net/l/1/ppid/pos");

        Assert.DoesNotContain("s=", qrUrl);
        Assert.StartsWith("https://qrcodegenerator.api.bancontact.net/qrcode?f=PNG&c=", qrUrl);
    }
}
