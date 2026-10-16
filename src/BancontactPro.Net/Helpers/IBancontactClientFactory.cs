using System.Security.Cryptography;
using BancontactPro.Http;
using BancontactPro.Payments;
using BancontactPro.Reconciliation;
using BancontactPro.Refunds;
using BancontactPro.Signing;
using Microsoft.Extensions.Options;

namespace BancontactPro.Helpers;

/// <summary>
/// Hands out API clients bound to one <see cref="BancontactProduct"/>: its API key, and for signed
/// calls its PPID. Bancontact issues credentials per product type, so a payment created with the
/// Static QR client must also be fetched, cancelled and refunded with Static QR clients.
/// </summary>
/// <remarks>
/// <para>
/// Every method reads the current credentials from the <see cref="IBancontactCredentialsProvider"/>,
/// so a change of credentials or environment applies to the next client you ask for. A client keeps
/// the credentials it was created with: ask for one per operation rather than holding on to it.
/// </para>
/// <para>
/// Asking for a product without a complete API key and PPID pair throws
/// <see cref="BancontactProductNotConfiguredException"/>; asking for a Refund or Reconciliation
/// client without a signing key throws <see cref="BancontactSigningKeyNotConfiguredException"/>.
/// Either way nothing is sent to Bancontact.
/// </para>
/// </remarks>
public interface IBancontactClientFactory
{
    /// <summary>Whether <paramref name="product"/> has a complete API key and PPID pair.</summary>
    Task<bool> IsConfiguredAsync(BancontactProduct product, CancellationToken cancellationToken = default);

    /// <summary>A Payment API client for <paramref name="product"/>, authenticated with its API key.</summary>
    /// <exception cref="BancontactProductNotConfiguredException">The product has no complete API key and PPID pair.</exception>
    Task<IPaymentClient> PaymentsAsync(BancontactProduct product, CancellationToken cancellationToken = default);

    /// <summary>A Refund API client for <paramref name="product"/>, JWS-signed with its PPID as the <c>sub</c> claim.</summary>
    /// <exception cref="BancontactProductNotConfiguredException">The product has no complete API key and PPID pair.</exception>
    /// <exception cref="BancontactSigningKeyNotConfiguredException">No signing key is configured.</exception>
    Task<IRefundClient> RefundsAsync(BancontactProduct product, CancellationToken cancellationToken = default);

    /// <summary>A Reconciliation API client for <paramref name="product"/>, JWS-signed with its PPID as the <c>sub</c> claim.</summary>
    /// <exception cref="BancontactProductNotConfiguredException">The product has no complete API key and PPID pair.</exception>
    /// <exception cref="BancontactSigningKeyNotConfiguredException">No signing key is configured.</exception>
    Task<IReconciliationClient> ReconciliationAsync(BancontactProduct product, CancellationToken cancellationToken = default);
}

/// <remarks>
/// Nothing credential-bearing is cached. The pooled <see cref="HttpClient"/> pipeline behind
/// <see cref="ClientNames.Api"/> holds only the primary handler; the handler that adds the API key
/// or the signature, the base address, the timeout and the user agent are created per call from that
/// call's credentials snapshot.
/// </remarks>
internal sealed class BancontactClientFactory : IBancontactClientFactory
{
    private static readonly TimeSpan DefaultHttpTimeout = TimeSpan.FromSeconds(100);
    private const string DefaultUserAgent = "BancontactPro.Net/0.x (+https://github.com/DeWall88/BancontactPro.Net)";

    private readonly CredentialsAccessor _credentials;
    private readonly IHttpMessageHandlerFactory _handlerFactory;

    // Parsing a PEM costs more than a request needs, so the last signing key is kept, keyed on its content:
    // a changed key (or kid) simply misses and replaces it. The replaced key is left to the finalizer, since
    // a call that started before the change may still be signing with it.
    private volatile CachedSigningKey? _signingKey;

    public BancontactClientFactory(CredentialsAccessor credentials, IHttpMessageHandlerFactory handlerFactory)
    {
        _credentials = credentials;
        _handlerFactory = handlerFactory;
    }

    public async Task<bool> IsConfiguredAsync(BancontactProduct product, CancellationToken cancellationToken = default) =>
        (await _credentials.GetAsync(cancellationToken).ConfigureAwait(false)).GetCredentials(product) is not null;

    public async Task<IPaymentClient> PaymentsAsync(BancontactProduct product, CancellationToken cancellationToken = default)
    {
        var credentials = await _credentials.GetAsync(cancellationToken).ConfigureAwait(false);
        var pair = Require(credentials, product);
        return new PaymentClient(CreateHttpClient(credentials.Environment, new BancontactApiKeyHandler(pair.ApiKey!)));
    }

    public async Task<IRefundClient> RefundsAsync(BancontactProduct product, CancellationToken cancellationToken = default) =>
        new RefundClient(await CreateSignedHttpClientAsync(product, cancellationToken).ConfigureAwait(false));

    public async Task<IReconciliationClient> ReconciliationAsync(BancontactProduct product, CancellationToken cancellationToken = default) =>
        new ReconciliationClient(await CreateSignedHttpClientAsync(product, cancellationToken).ConfigureAwait(false));

    private async Task<HttpClient> CreateSignedHttpClientAsync(BancontactProduct product, CancellationToken cancellationToken)
    {
        var credentials = await _credentials.GetAsync(cancellationToken).ConfigureAwait(false);
        var pair = Require(credentials, product);
        var signingKey = credentials.SigningKey is { } key && !string.IsNullOrWhiteSpace(key.Kid) && !string.IsNullOrWhiteSpace(key.PrivateKeyPem)
            ? key
            : throw new BancontactSigningKeyNotConfiguredException();

        var handler = new BancontactSigningHandler(
            GetSigningKeyProvider(signingKey),
            Options.Create(new BancontactSigningOptions
            {
                ApiKey = pair.ApiKey!,
                MerchantProfileId = pair.MerchantProfileId!,
                Issuer = credentials.Issuer,
            }));
        return CreateHttpClient(credentials.Environment, handler);
    }

    private static BancontactProductCredentials Require(BancontactCredentials credentials, BancontactProduct product) =>
        credentials.GetCredentials(product) ?? throw new BancontactProductNotConfiguredException(product);

    private HttpClient CreateHttpClient(BancontactEnvironment environment, DelegatingHandler credentialHandler)
    {
        credentialHandler.InnerHandler = _handlerFactory.CreateHandler(ClientNames.Api);

        // The pooled pipeline is shared, so the client must not dispose it.
        var client = new HttpClient(credentialHandler, disposeHandler: false)
        {
            BaseAddress = BancontactEndpoints.GetMerchantApiBaseUri(environment),
            Timeout = DefaultHttpTimeout,
        };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", DefaultUserAgent);
        return client;
    }

    private EcPemRequestSigningKeyProvider GetSigningKeyProvider(BancontactSigningKey key)
    {
        if (_signingKey is { } cached && cached.Kid == key.Kid && cached.Pem == key.PrivateKeyPem)
        {
            return cached.Provider;
        }

        EcPemRequestSigningKeyProvider provider;
        try
        {
            provider = new EcPemRequestSigningKeyProvider(key.Kid, key.PrivateKeyPem);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            throw new InvalidOperationException(
                $"The signing key '{key.Kid}' is not a valid PEM-encoded EC (P-256) private key, so Refund and Reconciliation requests cannot be signed.", ex);
        }

        _signingKey = new CachedSigningKey(key.Kid, key.PrivateKeyPem, provider);
        return provider;
    }

    private sealed record CachedSigningKey(string Kid, string Pem, EcPemRequestSigningKeyProvider Provider);
}

/// <summary>The named <see cref="HttpClient"/> pipelines behind the library's clients.</summary>
internal static class ClientNames
{
    /// <summary>The pipeline of every Payment, Refund and Reconciliation client: the primary handler only.</summary>
    public const string Api = "BancontactPro.Api";

    /// <summary>The pipeline used to fetch Bancontact's JWKS.</summary>
    public const string Jwks = "BancontactPro.Jwks";
}
