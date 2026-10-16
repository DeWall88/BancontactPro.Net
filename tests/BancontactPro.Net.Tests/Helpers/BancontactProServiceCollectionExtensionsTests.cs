using System.Net;
using System.Security.Cryptography;
using BancontactPro.Helpers;
using BancontactPro.Signing;
using BancontactPro.Webhooks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BancontactPro.Net.Tests.Helpers;

/// <summary>Registration from configuration (<c>AddBancontactProIntegration</c>): static settings, validated on start.</summary>
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
        Assert.IsType<ConfigurationCredentialsProvider>(provider.GetRequiredService<IBancontactCredentialsProvider>());
    }

    [Fact]
    public void AddBancontactProIntegration_InvalidConfiguration_ThrowsOnOptionsAccess()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var (provider, _) = Build(configuration);
        using var _p = provider;

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<BancontactProOptions>>().Value);
    }

    [Fact]
    public void AddBancontactProIntegration_NoProductConfigured_FailsStartupValidation()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var (provider, _) = Build(configuration);
        using var _p = provider;

        var failure = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
        Assert.Contains(failure.Failures, message => message.Contains("at least one product"));
    }

    [Fact]
    public void AddBancontactProIntegration_ValidConfiguration_PassesStartupValidation()
    {
        var (provider, _) = Build(BuildConfiguration());
        using var _p = provider;

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Theory]
    [InlineData("Preprod", "jwks.preprod.bancontact.net")]
    [InlineData("Production", "jwks.bancontact.net")]
    public async Task AddBancontactProIntegration_JwksClientFollowsEnvironment(string environment, string expectedHost)
    {
        var (provider, handler) = Build(BuildConfiguration(environment));
        using var _p = provider;

        // The canned empty response isn't a JWKS; only the address that was asked matters here.
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => provider.GetRequiredService<IJwksClient>().GetKeyAsync("kid"));

        Assert.Equal(expectedHost, handler.LastRequest!.RequestUri!.Host);
    }

    [Fact]
    public async Task Factory_ReportsWhichProductsAreConfigured()
    {
        var (provider, _) = Build(BuildConfiguration());
        using var _p = provider;
        var factory = provider.GetRequiredService<IBancontactClientFactory>();

        Assert.True(await factory.IsConfiguredAsync(BancontactProduct.StaticQr));
        Assert.True(await factory.IsConfiguredAsync(BancontactProduct.OnDisplay));
        Assert.False(await factory.IsConfiguredAsync(BancontactProduct.TopUp));
        Assert.False(await factory.IsConfiguredAsync(BancontactProduct.OnReceipt));
        Assert.False(await factory.IsConfiguredAsync(BancontactProduct.Invoice));
    }

    [Theory]
    [InlineData(BancontactProduct.StaticQr, "static-qr-key")]
    [InlineData(BancontactProduct.OnDisplay, "on-display-key")]
    public async Task Payments_UsesThatProductsApiKeyAndNoSignature(BancontactProduct product, string expectedApiKey)
    {
        var (provider, handler) = Build(BuildConfiguration());
        using var _p = provider;

        var payments = await provider.GetRequiredService<IBancontactClientFactory>().PaymentsAsync(product);
        await payments.CancelAsync("p1");

        Assert.Equal("merchant.api.preprod.bancontact.net", handler.LastRequest!.RequestUri!.Host);
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization?.Scheme);
        Assert.Equal(expectedApiKey, handler.LastRequest.Headers.Authorization?.Parameter);
        Assert.False(handler.LastRequest.Headers.Contains("Signature"));
        Assert.StartsWith("BancontactPro.Net/", handler.LastRequest.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task Payments_UsesProductionBaseAddress_WhenConfigured()
    {
        var (provider, handler) = Build(BuildConfiguration(environment: "Production"));
        using var _p = provider;

        var payments = await provider.GetRequiredService<IBancontactClientFactory>().PaymentsAsync(BancontactProduct.StaticQr);
        await payments.CancelAsync("p1");

        Assert.Equal("merchant.api.bancontact.net", handler.LastRequest!.RequestUri!.Host);
    }

    [Fact]
    public async Task Payments_WorksWithoutSigningKey()
    {
        var (provider, handler) = Build(BuildConfiguration(withSigningKey: false));
        using var _p = provider;

        var payments = await provider.GetRequiredService<IBancontactClientFactory>().PaymentsAsync(BancontactProduct.StaticQr);
        await payments.CancelAsync("p1");

        Assert.Equal("static-qr-key", handler.LastRequest!.Headers.Authorization?.Parameter);
    }

    [Theory]
    [InlineData(BancontactProduct.TopUp)]
    [InlineData(BancontactProduct.OnReceipt)]
    [InlineData(BancontactProduct.Invoice)]
    public async Task UnconfiguredProduct_EveryClientThrowsAndNothingIsSent(BancontactProduct product)
    {
        var (provider, handler) = Build(BuildConfiguration());
        using var _p = provider;
        var factory = provider.GetRequiredService<IBancontactClientFactory>();

        var payments = await Assert.ThrowsAsync<BancontactProductNotConfiguredException>(() => factory.PaymentsAsync(product));
        var refunds = await Assert.ThrowsAsync<BancontactProductNotConfiguredException>(() => factory.RefundsAsync(product));
        var reconciliation = await Assert.ThrowsAsync<BancontactProductNotConfiguredException>(() => factory.ReconciliationAsync(product));

        Assert.All([payments, refunds, reconciliation], ex => Assert.Equal(product, ex.Product));
        Assert.Contains($"Products:{product}:ApiKey", payments.Message);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task WithoutSigningKey_RefundAndReconciliationThrow()
    {
        var (provider, _) = Build(BuildConfiguration(withSigningKey: false));
        using var _p = provider;
        var factory = provider.GetRequiredService<IBancontactClientFactory>();

        var refunds = await Assert.ThrowsAsync<BancontactSigningKeyNotConfiguredException>(() => factory.RefundsAsync(BancontactProduct.StaticQr));
        await Assert.ThrowsAsync<BancontactSigningKeyNotConfiguredException>(() => factory.ReconciliationAsync(BancontactProduct.StaticQr));
        Assert.Contains("PrivateKeyPem", refunds.Message);
    }

    [Fact]
    public async Task Refunds_AreSignedWithThatProductsPpid()
    {
        var (provider, handler) = Build(BuildConfiguration());
        using var _p = provider;

        try
        {
            var refunds = await provider.GetRequiredService<IBancontactClientFactory>().RefundsAsync(BancontactProduct.OnDisplay);
            await refunds.GetAsync("p1", "r1");
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
