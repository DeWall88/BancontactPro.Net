using BancontactPro.Http;
using BancontactPro.Signing;
using BancontactPro.Webhooks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BancontactPro.Helpers;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/> that register the Bancontact Pro
/// integration: options, per-product API clients, request signing, and JWKS-based callback
/// verification.
/// </summary>
public static class BancontactProServiceCollectionExtensions
{
    private static readonly TimeSpan DefaultHttpTimeout = TimeSpan.FromSeconds(100);
    private const string DefaultUserAgent = "BancontactPro.Net/0.x (+https://github.com/DeWall88/BancontactPro.Net)";

    /// <summary>
    /// Registers the full Bancontact Pro integration: <see cref="BancontactProOptions"/> (bound
    /// and validated on startup), <see cref="IBancontactClientFactory"/> for per-product Payment,
    /// Refund and Reconciliation clients, and the JWKS client and callback verifier.
    /// </summary>
    /// <remarks>
    /// Each product needs its own API key and PPID under <c>BancontactPro:Products</c>. Payment
    /// clients send the product's API key; Refund and Reconciliation clients are JWS-signed and
    /// also need <see cref="BancontactProOptions.Kid"/> and <see cref="BancontactProOptions.PrivateKeyPem"/>.
    /// </remarks>
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
            return options.HasSigningKey
                ? new EcPemRequestSigningKeyProvider(options.Kid!, options.PrivateKeyPem!)
                : throw new BancontactSigningKeyNotConfiguredException();
        });

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<BancontactProOptions>>().Value;
            return Options.Create(new JwksClientOptions { JwksUri = GetJwksUri(options.Environment) });
        });
        services.AddHttpClient<IJwksClient, JwksClient>();

        services.AddSingleton<BancontactSignatureVerifier>();
        services.AddSingleton<BancontactCallbackVerifier>();

        // One named HttpClient per product and API, each with that product's credentials. The Payment
        // API accepts the API key or a signature, and the API key alone is enough. Refund and
        // Reconciliation accept only a signature. BancontactClientFactory checks the configuration
        // before creating a client, so a handler below never sees a missing pair or signing key.
        foreach (var product in Enum.GetValues<BancontactProduct>())
        {
            services.AddHttpClient(ClientNames.Payments(product), ConfigureApiClient)
                .AddHttpMessageHandler(sp => new BancontactApiKeyHandler(GetCredentials(sp, product).ApiKey!));
            services.AddHttpClient(ClientNames.Refunds(product), ConfigureApiClient)
                .AddHttpMessageHandler(sp => CreateSigningHandler(sp, product));
            services.AddHttpClient(ClientNames.Reconciliation(product), ConfigureApiClient)
                .AddHttpMessageHandler(sp => CreateSigningHandler(sp, product));
        }

        services.AddSingleton<IBancontactClientFactory, BancontactClientFactory>();

        return services;
    }

    private static BancontactProductCredentials GetCredentials(IServiceProvider serviceProvider, BancontactProduct product) =>
        serviceProvider.GetRequiredService<IOptions<BancontactProOptions>>().Value.GetCredentials(product)
            ?? throw new BancontactProductNotConfiguredException(product);

    private static BancontactSigningHandler CreateSigningHandler(IServiceProvider serviceProvider, BancontactProduct product)
    {
        var options = serviceProvider.GetRequiredService<IOptions<BancontactProOptions>>().Value;
        var credentials = GetCredentials(serviceProvider, product);
        return new BancontactSigningHandler(
            serviceProvider.GetRequiredService<IRequestSigningKeyProvider>(),
            Options.Create(new BancontactSigningOptions
            {
                ApiKey = credentials.ApiKey!,
                MerchantProfileId = credentials.MerchantProfileId!,
                Issuer = options.Issuer,
            }));
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
