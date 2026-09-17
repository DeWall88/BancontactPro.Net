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

    private static BancontactProOptions ValidOptions() => new()
    {
        ApiKey = "api-key",
        MerchantProfileId = "profile-1",
        Kid = "kid-1",
        PrivateKeyPem = GenerateValidPem(),
    };

    [Fact]
    public void Validate_WithAllRequiredFields_Succeeds()
    {
        var result = new BancontactProOptionsValidator().Validate(null, ValidOptions());

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(nameof(BancontactProOptions.ApiKey))]
    [InlineData(nameof(BancontactProOptions.MerchantProfileId))]
    [InlineData(nameof(BancontactProOptions.Kid))]
    public void Validate_MissingRequiredField_Fails(string propertyName)
    {
        var options = ValidOptions();
        typeof(BancontactProOptions).GetProperty(propertyName)!.SetValue(options, null);

        var result = new BancontactProOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(propertyName, result.FailureMessage);
    }

    [Fact]
    public void Validate_MissingPrivateKeyPem_Fails()
    {
        var options = ValidOptions();
        options.PrivateKeyPem = null;

        var result = new BancontactProOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(nameof(BancontactProOptions.PrivateKeyPem), result.FailureMessage);
    }

    [Fact]
    public void Validate_MalformedPrivateKeyPem_Fails()
    {
        var options = ValidOptions();
        options.PrivateKeyPem = "not a real pem";

        var result = new BancontactProOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("valid PEM-encoded EC private key", result.FailureMessage);
    }
}
