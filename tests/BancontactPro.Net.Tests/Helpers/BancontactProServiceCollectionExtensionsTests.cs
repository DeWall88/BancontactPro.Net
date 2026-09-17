using System.Net;
using System.Security.Cryptography;
using BancontactPro.Helpers;
using BancontactPro.Payments;
using BancontactPro.Reconciliation;
using BancontactPro.Refunds;
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
        public Func<HttpResponseMessage> ResponseFactory { get; set; } = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(ResponseFactory());
        }
    }

    private static string GenerateValidPem()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return key.ExportPkcs8PrivateKeyPem();
    }

    private static IConfiguration BuildConfiguration(string environment = "Preprod") => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BancontactPro:ApiKey"] = "api-key",
            ["BancontactPro:MerchantProfileId"] = "profile-1",
            ["BancontactPro:Kid"] = "kid-1",
            ["BancontactPro:PrivateKeyPem"] = GenerateValidPem(),
            ["BancontactPro:Environment"] = environment,
        })
        .Build();

    [Fact]
    public void AddBancontactProIntegration_ResolvesAllRegisteredServices()
    {
        var services = new ServiceCollection();
        services.AddBancontactProIntegration(BuildConfiguration());
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IPaymentClient>());
        Assert.NotNull(provider.GetRequiredService<IRefundClient>());
        Assert.NotNull(provider.GetRequiredService<IReconciliationClient>());
        Assert.NotNull(provider.GetRequiredService<IJwksClient>());
        Assert.NotNull(provider.GetRequiredService<BancontactCallbackVerifier>());
        Assert.NotNull(provider.GetRequiredService<IRequestSigningKeyProvider>());
    }

    [Fact]
    public void AddBancontactProIntegration_InvalidConfiguration_ThrowsOnOptionsAccess()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        services.AddBancontactProIntegration(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<BancontactProOptions>>().Value);
    }

    [Fact]
    public void AddBancontactProIntegration_JwksClientUsesPreprodUriByDefault()
    {
        var services = new ServiceCollection();
        services.AddBancontactProIntegration(BuildConfiguration());
        using var provider = services.BuildServiceProvider();

        var jwksOptions = provider.GetRequiredService<IOptions<JwksClientOptions>>().Value;

        Assert.Equal("https://jwks.preprod.bancontact.net/", jwksOptions.JwksUri.ToString());
    }

    [Fact]
    public void AddBancontactProIntegration_JwksClientUsesProductionUri_WhenConfigured()
    {
        var services = new ServiceCollection();
        services.AddBancontactProIntegration(BuildConfiguration(environment: "Production"));
        using var provider = services.BuildServiceProvider();

        var jwksOptions = provider.GetRequiredService<IOptions<JwksClientOptions>>().Value;

        Assert.Equal("https://jwks.bancontact.net/", jwksOptions.JwksUri.ToString());
    }

    [Fact]
    public async Task AddBancontactProIntegration_PaymentClient_UsesPreprodBaseAddressAndSignsRequests()
    {
        var services = new ServiceCollection();
        services.AddBancontactProIntegration(BuildConfiguration());

        var handler = new CapturingHandler();
        services.AddHttpClient<IPaymentClient, PaymentClient>().ConfigurePrimaryHttpMessageHandler(() => handler);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IPaymentClient>();

        handler.ResponseFactory = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
            {
              "paymentId": "p1", "createdAt": "2026-01-01T00:00:00.000Z", "expireAt": "2026-01-01T00:15:00.000Z",
              "currency": "EUR", "status": "PENDING", "creditor": {}, "amount": 100,
              "_links": { "self": {"href":"x"}, "deeplink": {"href":"x"}, "qrcode": {"href":"x"} }
            }
            """),
        };

        await client.GetAsync("p1");

        Assert.Equal("merchant.api.preprod.bancontact.net", handler.LastRequest!.RequestUri!.Host);
        Assert.NotEmpty(handler.LastRequest.Headers.GetValues("Signature"));
        Assert.Equal("api-key", handler.LastRequest.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task AddBancontactProIntegration_PaymentClient_UsesProductionBaseAddress_WhenConfigured()
    {
        var services = new ServiceCollection();
        services.AddBancontactProIntegration(BuildConfiguration(environment: "Production"));

        var handler = new CapturingHandler();
        services.AddHttpClient<IPaymentClient, PaymentClient>().ConfigurePrimaryHttpMessageHandler(() => handler);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IPaymentClient>();

        await client.CancelAsync("p1");

        Assert.Equal("merchant.api.bancontact.net", handler.LastRequest!.RequestUri!.Host);
    }
}
