using System.Net;
using System.Security.Cryptography;
using BancontactPro.Helpers;
using BancontactPro.Signing;
using BancontactPro.Webhooks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BancontactPro.Net.Tests.Helpers;

public class BancontactProServiceCollectionExtensionsTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }

    private static string GenerateValidPem()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return key.ExportPkcs8PrivateKeyPem();
    }

    private static IConfiguration BuildConfiguration(string environment = "Preprod", bool withSigningKey = true)
    {
        var values = new Dictionary<string, string?>
        {
            ["BancontactPro:Products:StaticQr:ApiKey"] = "static-qr-key",
            ["BancontactPro:Products:StaticQr:MerchantProfileId"] = "static-qr-ppid",
            ["BancontactPro:Products:OnDisplay:ApiKey"] = "on-display-key",
            ["BancontactPro:Products:OnDisplay:MerchantProfileId"] = "on-display-ppid",
            // Declared but blank, as a compose file with every product's variables would bind it.
            ["BancontactPro:Products:TopUp:ApiKey"] = "",
            ["BancontactPro:Products:TopUp:MerchantProfileId"] = "",
            ["BancontactPro:Environment"] = environment,
        };

        if (withSigningKey)
        {
            values["BancontactPro:Kid"] = "kid-1";
            values["BancontactPro:PrivateKeyPem"] = GenerateValidPem();
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static (ServiceProvider provider, CapturingHandler handler) Build(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddBancontactProIntegration(configuration);

        var handler = new CapturingHandler();
        services.ConfigureHttpClientDefaults(builder => builder.ConfigurePrimaryHttpMessageHandler(() => handler));

        return (services.BuildServiceProvider(), handler);
    }

    [Fact]
    public void AddBancontactProIntegration_ResolvesSharedServices()
    {
        var (provider, _) = Build(BuildConfiguration());
        using var _p = provider;

        Assert.NotNull(provider.GetRequiredService<IBancontactClientFactory>());
        Assert.NotNull(provider.GetRequiredService<IJwksClient>());
        Assert.NotNull(provider.GetRequiredService<BancontactCallbackVerifier>());
        Assert.NotNull(provider.GetRequiredService<IRequestSigningKeyProvider>());
    }

    [Fact]
    public void AddBancontactProIntegration_InvalidConfiguration_ThrowsOnOptionsAccess()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var (provider, _) = Build(configuration);
        using var _p = provider;

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<BancontactProOptions>>().Value);
    }

    [Theory]
    [InlineData("Preprod", "https://jwks.preprod.bancontact.net/")]
    [InlineData("Production", "https://jwks.bancontact.net/")]
    public void AddBancontactProIntegration_JwksClientFollowsEnvironment(string environment, string expected)
    {
        var (provider, _) = Build(BuildConfiguration(environment));
        using var _p = provider;

        Assert.Equal(expected, provider.GetRequiredService<IOptions<JwksClientOptions>>().Value.JwksUri.ToString());
    }

    [Fact]
    public void Factory_ReportsWhichProductsAreConfigured()
    {
        var (provider, _) = Build(BuildConfiguration());
        using var _p = provider;
        var factory = provider.GetRequiredService<IBancontactClientFactory>();

        Assert.True(factory.IsConfigured(BancontactProduct.StaticQr));
        Assert.True(factory.IsConfigured(BancontactProduct.OnDisplay));
        Assert.False(factory.IsConfigured(BancontactProduct.TopUp));
        Assert.False(factory.IsConfigured(BancontactProduct.OnReceipt));
        Assert.False(factory.IsConfigured(BancontactProduct.Invoice));
    }

    [Theory]
    [InlineData(BancontactProduct.StaticQr, "static-qr-key")]
    [InlineData(BancontactProduct.OnDisplay, "on-display-key")]
    public async Task Payments_UsesThatProductsApiKeyAndNoSignature(BancontactProduct product, string expectedApiKey)
    {
        var (provider, handler) = Build(BuildConfiguration());
        using var _p = provider;

        await provider.GetRequiredService<IBancontactClientFactory>().Payments(product).CancelAsync("p1");

        Assert.Equal("merchant.api.preprod.bancontact.net", handler.LastRequest!.RequestUri!.Host);
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization?.Scheme);
        Assert.Equal(expectedApiKey, handler.LastRequest.Headers.Authorization?.Parameter);
        Assert.False(handler.LastRequest.Headers.Contains("Signature"));
    }

    [Fact]
    public async Task Payments_UsesProductionBaseAddress_WhenConfigured()
    {
        var (provider, handler) = Build(BuildConfiguration(environment: "Production"));
        using var _p = provider;

        await provider.GetRequiredService<IBancontactClientFactory>().Payments(BancontactProduct.StaticQr).CancelAsync("p1");

        Assert.Equal("merchant.api.bancontact.net", handler.LastRequest!.RequestUri!.Host);
    }

    [Fact]
    public async Task Payments_WorksWithoutSigningKey()
    {
        var (provider, handler) = Build(BuildConfiguration(withSigningKey: false));
        using var _p = provider;

        await provider.GetRequiredService<IBancontactClientFactory>().Payments(BancontactProduct.StaticQr).CancelAsync("p1");

        Assert.Equal("static-qr-key", handler.LastRequest!.Headers.Authorization?.Parameter);
    }

    [Theory]
    [InlineData(BancontactProduct.TopUp)]
    [InlineData(BancontactProduct.OnReceipt)]
    [InlineData(BancontactProduct.Invoice)]
    public void UnconfiguredProduct_EveryClientThrowsAndNothingIsSent(BancontactProduct product)
    {
        var (provider, handler) = Build(BuildConfiguration());
        using var _p = provider;
        var factory = provider.GetRequiredService<IBancontactClientFactory>();

        var payments = Assert.Throws<BancontactProductNotConfiguredException>(() => factory.Payments(product));
        var refunds = Assert.Throws<BancontactProductNotConfiguredException>(() => factory.Refunds(product));
        var reconciliation = Assert.Throws<BancontactProductNotConfiguredException>(() => factory.Reconciliation(product));

        Assert.All([payments, refunds, reconciliation], ex => Assert.Equal(product, ex.Product));
        Assert.Contains($"Products:{product}:ApiKey", payments.Message);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public void WithoutSigningKey_RefundAndReconciliationThrow()
    {
        var (provider, _) = Build(BuildConfiguration(withSigningKey: false));
        using var _p = provider;
        var factory = provider.GetRequiredService<IBancontactClientFactory>();

        var refunds = Assert.Throws<BancontactSigningKeyNotConfiguredException>(() => factory.Refunds(BancontactProduct.StaticQr));
        Assert.Throws<BancontactSigningKeyNotConfiguredException>(() => factory.Reconciliation(BancontactProduct.StaticQr));
        Assert.Contains("PrivateKeyPem", refunds.Message);
    }

    [Fact]
    public async Task Refunds_AreSignedWithThatProductsPpid()
    {
        var (provider, handler) = Build(BuildConfiguration());
        using var _p = provider;

        try
        {
            await provider.GetRequiredService<IBancontactClientFactory>().Refunds(BancontactProduct.OnDisplay).GetAsync("p1", "r1");
        }
        catch (Exception)
        {
            // The canned empty response doesn't deserialize; only the outgoing request matters here.
        }

        var signature = handler.LastRequest!.Headers.GetValues("Signature").Single();
        var header = System.Text.Json.JsonDocument.Parse(System.Buffers.Text.Base64Url.DecodeFromChars(signature.Split('.')[0]));
        Assert.Equal("on-display-ppid", header.RootElement.GetProperty("https://payconiq.com/sub").GetString());
        Assert.Equal("kid-1", header.RootElement.GetProperty("kid").GetString());
        Assert.Equal("on-display-key", handler.LastRequest.Headers.Authorization?.Parameter);
    }
}
