using BancontactPro.Payments;
using BancontactPro.Reconciliation;
using BancontactPro.Refunds;
using BancontactPro.Signing;
using BancontactPro.Webhooks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BancontactPro.Helpers;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/> that register the Bancontact Pro
/// integration: options, request signing, JWKS-based callback verification, and the Payment,
/// Refund, and Reconciliation typed clients.
/// </summary>
public static class BancontactProServiceCollectionExtensions
{
    private static readonly TimeSpan DefaultHttpTimeout = TimeSpan.FromSeconds(100);
    private const string DefaultUserAgent = "BancontactPro.Net/0.x (+https://github.com/DeWall88/BancontactPro.Net)";

    /// <summary>
    /// Registers the full Bancontact Pro integration: <see cref="BancontactProOptions"/> (bound
    /// and validated on startup), the signing key provider and <see cref="BancontactSigningHandler"/>,
    /// the JWKS client and callback verifier, and typed, signed <see cref="HttpClient"/>s for
    /// <see cref="IPaymentClient"/>, <see cref="IRefundClient"/>, and <see cref="IReconciliationClient"/>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="configuration">The application configuration used to bind <see cref="BancontactProOptions"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
    public static IServiceCollection AddBancontactProIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<BancontactProOptions>, BancontactProOptionsValidator>();
        services.AddOptions<BancontactProOptions>()
            .Bind(configuration.GetSection(BancontactProOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IRequestSigningKeyProvider>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<BancontactProOptions>>().Value;
            return new EcPemRequestSigningKeyProvider(options.Kid!, options.PrivateKeyPem!);
        });

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<BancontactProOptions>>().Value;
            return Options.Create(new BancontactSigningOptions
            {
                ApiKey = options.ApiKey!,
                MerchantProfileId = options.MerchantProfileId!,
                Issuer = options.Issuer,
            });
        });
        services.AddTransient<BancontactSigningHandler>();

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<BancontactProOptions>>().Value;
            return Options.Create(new JwksClientOptions { JwksUri = GetJwksUri(options.Environment) });
        });
        services.AddHttpClient<IJwksClient, JwksClient>();

        services.AddSingleton<BancontactSignatureVerifier>();
        services.AddSingleton<BancontactCallbackVerifier>();

        services.AddHttpClient<IPaymentClient, PaymentClient>(ConfigureApiClient)
            .AddHttpMessageHandler<BancontactSigningHandler>();
        services.AddHttpClient<IRefundClient, RefundClient>(ConfigureApiClient)
            .AddHttpMessageHandler<BancontactSigningHandler>();
        services.AddHttpClient<IReconciliationClient, ReconciliationClient>(ConfigureApiClient)
            .AddHttpMessageHandler<BancontactSigningHandler>();

        return services;
    }

    private static void ConfigureApiClient(IServiceProvider serviceProvider, HttpClient client)
    {
        var options = serviceProvider.GetRequiredService<IOptions<BancontactProOptions>>().Value;
        client.BaseAddress = GetMerchantApiBaseUri(options.Environment);
        client.Timeout = DefaultHttpTimeout;
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", DefaultUserAgent);
    }

    private static Uri GetMerchantApiBaseUri(BancontactEnvironment environment) => environment switch
    {
        BancontactEnvironment.Production => new Uri("https://merchant.api.bancontact.net/"),
        _ => new Uri("https://merchant.api.preprod.bancontact.net/"),
    };

    private static Uri GetJwksUri(BancontactEnvironment environment) => environment switch
    {
        BancontactEnvironment.Production => new Uri("https://jwks.bancontact.net/"),
        _ => new Uri("https://jwks.preprod.bancontact.net/"),
    };
}
