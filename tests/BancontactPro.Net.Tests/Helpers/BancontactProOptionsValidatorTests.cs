using System.Security.Cryptography;
using BancontactPro.Helpers;

namespace BancontactPro.Net.Tests.Helpers;

public class BancontactProOptionsValidatorTests
{
    private static string GenerateValidPem()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return key.ExportPkcs8PrivateKeyPem();
    }

    private static BancontactProductCredentials Pair(string suffix = "1") => new() { ApiKey = $"api-key-{suffix}", MerchantProfileId = $"ppid-{suffix}" };

    private static BancontactProOptions ValidOptions() => new()
    {
        Products = { [BancontactProduct.StaticQr] = Pair() },
        Kid = "kid-1",
        PrivateKeyPem = GenerateValidPem(),
    };

    private static ValidateOptionsResultWrapper Validate(BancontactProOptions options) =>
        new(new BancontactProOptionsValidator().Validate(null, options));

    private sealed record ValidateOptionsResultWrapper(Microsoft.Extensions.Options.ValidateOptionsResult Result)
    {
        public bool Succeeded => Result.Succeeded;
        public string FailureMessage => Result.FailureMessage ?? string.Empty;
    }

    [Fact]
    public void Validate_ProductAndSigningKey_Succeeds()
    {
        Assert.True(Validate(ValidOptions()).Succeeded);
    }

    [Fact]
    public void Validate_ProductPairOnly_Succeeds()
    {
        var options = new BancontactProOptions { Products = { [BancontactProduct.OnDisplay] = Pair() } };

        Assert.True(Validate(options).Succeeded);
    }

    [Fact]
    public void Validate_SeveralProducts_Succeeds()
    {
        var options = new BancontactProOptions
        {
            Products =
            {
                [BancontactProduct.StaticQr] = Pair("qr"),
                [BancontactProduct.OnDisplay] = Pair("display"),
                [BancontactProduct.TopUp] = Pair("topup"),
                [BancontactProduct.Invoice] = Pair("invoice"),
            },
        };

        Assert.True(Validate(options).Succeeded);
    }

    [Fact]
    public void Validate_NoProducts_Fails()
    {
        var result = Validate(new BancontactProOptions());

        Assert.False(result.Succeeded);
        Assert.Contains("at least one product", result.FailureMessage);
    }

    [Fact]
    public void Validate_OnlyEmptyProductEntries_FailsAsNoProducts()
    {
        var options = new BancontactProOptions { Products = { [BancontactProduct.TopUp] = new() { ApiKey = "", MerchantProfileId = " " } } };

        var result = Validate(options);

        Assert.False(result.Succeeded);
        Assert.Contains("at least one product", result.FailureMessage);
    }

    [Fact]
    public void Validate_EmptyEntryNextToACompleteOne_IsIgnored()
    {
        var options = new BancontactProOptions
        {
            Products =
            {
                [BancontactProduct.StaticQr] = Pair(),
                [BancontactProduct.TopUp] = new() { ApiKey = "", MerchantProfileId = "" },
            },
        };

        Assert.True(Validate(options).Succeeded);
    }

    [Fact]
    public void Validate_ApiKeyWithoutPpid_Fails()
    {
        var options = new BancontactProOptions { Products = { [BancontactProduct.StaticQr] = new() { ApiKey = "api-key" } } };

        var result = Validate(options);

        Assert.False(result.Succeeded);
        Assert.Contains("Products:StaticQr:MerchantProfileId", result.FailureMessage);
    }

    [Fact]
    public void Validate_PpidWithoutApiKey_Fails()
    {
        var options = new BancontactProOptions { Products = { [BancontactProduct.OnReceipt] = new() { MerchantProfileId = "ppid" } } };

        var result = Validate(options);

        Assert.False(result.Succeeded);
        Assert.Contains("Products:OnReceipt:ApiKey", result.FailureMessage);
    }

    [Fact]
    public void Validate_KidWithoutPrivateKeyPem_Fails()
    {
        var options = ValidOptions();
        options.PrivateKeyPem = null;

        var result = Validate(options);

        Assert.False(result.Succeeded);
        Assert.Contains(nameof(BancontactProOptions.PrivateKeyPem), result.FailureMessage);
    }

    [Fact]
    public void Validate_PrivateKeyPemWithoutKid_Fails()
    {
        var options = ValidOptions();
        options.Kid = null;

        var result = Validate(options);

        Assert.False(result.Succeeded);
        Assert.Contains(nameof(BancontactProOptions.Kid), result.FailureMessage);
    }

    [Fact]
    public void Validate_MalformedPrivateKeyPem_Fails()
    {
        var options = ValidOptions();
        options.PrivateKeyPem = "not a real pem";

        var result = Validate(options);

        Assert.False(result.Succeeded);
        Assert.Contains("valid PEM-encoded EC private key", result.FailureMessage);
    }
}
