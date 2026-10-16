using BancontactPro.Signing;
using BancontactPro.Webhooks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace BancontactPro.Helpers;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/> that register the Bancontact Pro
/// integration: per-product API clients, request signing, and JWKS-based callback verification.
/// </summary>
/// <remarks>
/// There are two ways in, over one shared core. <see cref="AddBancontactProIntegration"/> reads static
/// settings from configuration and validates them on startup. <c>AddBancontactPro</c> takes an
/// <see cref="IBancontactCredentialsProvider"/> for hosts whose credentials can change at runtime or
/// may be absent at startup. Use one of them, not both.
/// </remarks>
public static class BancontactProServiceCollectionExtensions
{
    /// <summary>
    /// Registers the integration for static settings: <see cref="BancontactProOptions"/> is bound from the
    /// <c>BancontactPro</c> section and validated on startup, and <see cref="ConfigurationCredentialsProvider"/>
    /// supplies it as the credentials. The host does not start without a configured product.
    /// </summary>
    /// <remarks>
    /// Each product needs its own API key and PPID under <c>BancontactPro:Products</c>. Payment
    /// clients send the product's API key; Refund and Reconciliation clients are JWS-signed and
    /// also need <see cref="BancontactProOptions.Kid"/> and <see cref="BancontactProOptions.PrivateKeyPem"/>.
    /// The settings are read once; for credentials that change at runtime, or a host that must start
    /// without any, use <see cref="AddBancontactPro{TProvider}"/>.
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

        return services.AddBancontactPro<ConfigurationCredentialsProvider>(ServiceLifetime.Singleton);
    }

    /// <summary>
    /// Registers the integration for a host that supplies its own credentials through
    /// <typeparamref name="TProvider"/>. Nothing is validated or read at startup, so a host with nothing
    /// configured still starts; clients report "not configured" when they are used. Every call reads the
    /// provider again, so a change applies to the next call.
    /// </summary>
    /// <typeparam name="TProvider">The credentials provider, resolved from the container.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="providerLifetime">
    /// The provider's lifetime. The library resolves it in a short scope of its own for each call, so a
    /// <see cref="ServiceLifetime.Scoped"/> provider (the default) can use a DbContext; choose
    /// <see cref="ServiceLifetime.Singleton"/> for a provider that caches.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
    public static IServiceCollection AddBancontactPro<TProvider>(this IServiceCollection services, ServiceLifetime providerLifetime = ServiceLifetime.Scoped)
        where TProvider : class, IBancontactCredentialsProvider
    {
        ArgumentNullException.ThrowIfNull(services);

        services.RemoveAll<IBancontactCredentialsProvider>();
        services.Add(new ServiceDescriptor(typeof(IBancontactCredentialsProvider), typeof(TProvider), providerLifetime));
        return AddCore(services);
    }

    /// <summary>
    /// Registers the integration like <see cref="AddBancontactPro{TProvider}"/>, creating the provider with
    /// <paramref name="providerFactory"/>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="providerFactory">Creates the provider; it receives the scope the library opened for the call.</param>
    /// <param name="providerLifetime">The provider's lifetime, as for <see cref="AddBancontactPro{TProvider}"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
    public static IServiceCollection AddBancontactPro(
        this IServiceCollection services,
        Func<IServiceProvider, IBancontactCredentialsProvider> providerFactory,
        ServiceLifetime providerLifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(providerFactory);

        services.RemoveAll<IBancontactCredentialsProvider>();
        services.Add(new ServiceDescriptor(typeof(IBancontactCredentialsProvider), providerFactory, providerLifetime));
        return AddCore(services);
    }

    // The part both entry points share. Nothing here reads a credential: the factory and the JWKS client ask
    // the provider on every call, and the one named pipeline holds no credential-bearing handler.
    private static IServiceCollection AddCore(IServiceCollection services)
    {
        services.AddHttpClient(ClientNames.Api);
        services.AddHttpClient(ClientNames.Jwks);

        services.TryAddSingleton<CredentialsAccessor>();
        services.TryAddSingleton<IBancontactClientFactory, BancontactClientFactory>();

        // One long-lived instance: it caches the keys, so a transient one (the typed-client default) would
        // fetch the JWKS again for every consumer. Each fetch takes a fresh HttpClient from the factory.
        services.TryAddSingleton<IJwksClient>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var credentials = sp.GetRequiredService<CredentialsAccessor>();
            return new JwksClient(
                () => httpClientFactory.CreateClient(ClientNames.Jwks),
                async cancellationToken => (await credentials.GetAsync(cancellationToken).ConfigureAwait(false)).Environment);
        });

        services.TryAddSingleton<BancontactSignatureVerifier>();
        services.TryAddSingleton<BancontactCallbackVerifier>();

        return services;
    }
}
