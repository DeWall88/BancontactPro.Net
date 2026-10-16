using BancontactPro.Helpers;
using Microsoft.Extensions.Options;

namespace BancontactPro.Net.Tests.Helpers;

public class CredentialsTypesTests
{
    [Theory]
    [InlineData(BancontactEnvironment.Preprod, "https://merchant.api.preprod.bancontact.net/", "https://jwks.preprod.bancontact.net/")]
    [InlineData(BancontactEnvironment.Production, "https://merchant.api.bancontact.net/", "https://jwks.bancontact.net/")]
    public void Endpoints_FollowTheEnvironment(BancontactEnvironment environment, string merchantApi, string jwks)
    {
        Assert.Equal(merchantApi, BancontactEndpoints.GetMerchantApiBaseUri(environment).ToString());
        Assert.Equal(jwks, BancontactEndpoints.GetJwksUri(environment).ToString());
    }

    [Fact]
    public async Task ConfigurationProvider_MapsTheOptionsOntoACredentialsSnapshot()
    {
        var options = new BancontactProOptions
        {
            Environment = BancontactEnvironment.Production,
            Issuer = "merchant-42",
            Kid = "kid-1",
            PrivateKeyPem = "pem",
            Products =
            {
                [BancontactProduct.StaticQr] = new BancontactProductCredentials { ApiKey = "key", MerchantProfileId = "ppid" },
                [BancontactProduct.TopUp] = new BancontactProductCredentials(),
            },
        };

        var credentials = await new ConfigurationCredentialsProvider(Options.Create(options)).GetCredentialsAsync();

        Assert.Equal(BancontactEnvironment.Production, credentials.Environment);
        Assert.Equal("merchant-42", credentials.Issuer);
        Assert.Equal(new BancontactSigningKey("kid-1", "pem"), credentials.SigningKey);
        Assert.Equal("key", credentials.GetCredentials(BancontactProduct.StaticQr)?.ApiKey);
        Assert.Null(credentials.GetCredentials(BancontactProduct.TopUp));
        Assert.Null(credentials.GetCredentials(BancontactProduct.OnDisplay));
    }

    [Fact]
    public async Task ConfigurationProvider_WithoutSigningKey_ReturnsNone()
    {
        var options = new BancontactProOptions { Kid = "kid-only" };

        var credentials = await new ConfigurationCredentialsProvider(Options.Create(options)).GetCredentialsAsync();

        Assert.Null(credentials.SigningKey);
    }

    [Fact]
    public void Credentials_Default_AreEmptyPreprod()
    {
        var credentials = new BancontactCredentials();

        Assert.Equal(BancontactEnvironment.Preprod, credentials.Environment);
        Assert.Empty(credentials.Products);
        Assert.Null(credentials.SigningKey);
        Assert.Equal("Payconiq", credentials.Issuer);
    }

    [Fact]
    public void SigningKey_ToString_NeverShowsThePrivateKey()
    {
        var text = new BancontactSigningKey("kid-1", "-----BEGIN PRIVATE KEY-----SECRET").ToString();

        Assert.Contains("kid-1", text);
        Assert.DoesNotContain("SECRET", text);
    }
}
