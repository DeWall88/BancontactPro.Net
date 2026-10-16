using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using BancontactPro.Helpers;

namespace BancontactPro.Signing;

/// <summary>
/// Fetches and caches Bancontact's JWKS. On a cache miss, refreshes once (to pick up key
/// rotation) before concluding a <c>kid</c> genuinely doesn't exist. Concurrent misses are
/// coalesced into a single in-flight fetch.
/// </summary>
/// <remarks>
/// <para>
/// The JWKS endpoint follows the <see cref="BancontactCredentials.Environment"/> the credentials provider
/// returns at the time of each lookup, and each environment has its own key cache, so switching between
/// Preprod and Production at runtime needs no restart and never serves one environment's keys for the other.
/// </para>
/// <para>
/// The keys are cached in this instance: keep one for the lifetime of the application (the DI registrations
/// do) and never create one per call, or every callback would fetch the JWKS again.
/// </para>
/// </remarks>
public sealed class JwksClient : IJwksClient
{
    private readonly Func<HttpClient> _httpClientSource;
    private readonly Func<CancellationToken, Task<BancontactEnvironment>> _environmentSource;
    private readonly ConcurrentDictionary<BancontactEnvironment, EnvironmentCache> _caches = new();

    /// <param name="httpClient">The client used to fetch the JWKS document.</param>
    /// <param name="credentialsProvider">Supplies the environment, and with it the JWKS endpoint, on every lookup.</param>
    public JwksClient(HttpClient httpClient, IBancontactCredentialsProvider credentialsProvider)
        : this(() => httpClient, async ct => (await credentialsProvider.GetCredentialsAsync(ct).ConfigureAwait(false)).Environment)
    {
    }

    /// <summary>
    /// Used by the DI registrations: a fresh <see cref="HttpClient"/> per fetch keeps the pooled handlers rotating
    /// (DNS changes) while the keys stay cached in this long-lived instance.
    /// </summary>
    internal JwksClient(Func<HttpClient> httpClientSource, Func<CancellationToken, Task<BancontactEnvironment>> environmentSource)
    {
        _httpClientSource = httpClientSource;
        _environmentSource = environmentSource;
    }

    /// <inheritdoc />
    public async Task<ECDsa?> GetKeyAsync(string kid, CancellationToken cancellationToken = default)
    {
        var environment = await _environmentSource(cancellationToken).ConfigureAwait(false);
        var cache = _caches.GetOrAdd(environment, static _ => new EnvironmentCache());

        if (cache.Keys.TryGetValue(kid, out var key))
        {
            return key;
        }

        var refreshed = await RefreshAsync(environment, cache).WaitAsync(cancellationToken).ConfigureAwait(false);
        return refreshed.TryGetValue(kid, out key) ? key : null;
    }

    private Task<Dictionary<string, ECDsa>> RefreshAsync(BancontactEnvironment environment, EnvironmentCache cache)
    {
        // Registers the in-flight marker *before* the fetch starts, rather than having the fetch
        // clear its own marker on completion: if the fetch completes synchronously (e.g. behind a
        // fake handler in tests, or simply a fast response), a self-clearing approach can run its
        // "clear" step before this method has even stored the marker, permanently wedging a stale
        // completed result in place. Setting it up-front removes that ordering hazard entirely.
        TaskCompletionSource<Dictionary<string, ECDsa>> tcs;
        lock (cache.Gate)
        {
            if (cache.RefreshInFlight is { } inFlight)
            {
                return inFlight.Task;
            }

            tcs = cache.RefreshInFlight = new TaskCompletionSource<Dictionary<string, ECDsa>>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        _ = FetchAndCompleteAsync(environment, cache, tcs);
        return tcs.Task;
    }

    private async Task FetchAndCompleteAsync(BancontactEnvironment environment, EnvironmentCache cache, TaskCompletionSource<Dictionary<string, ECDsa>> tcs)
    {
        try
        {
            var keys = await FetchAsync(environment).ConfigureAwait(false);
            cache.Keys = keys;
            lock (cache.Gate)
            {
                cache.RefreshInFlight = null;
            }

            tcs.SetResult(keys);
        }
        catch (Exception ex)
        {
            lock (cache.Gate)
            {
                cache.RefreshInFlight = null;
            }

            tcs.SetException(ex);
        }
    }

    private async Task<Dictionary<string, ECDsa>> FetchAsync(BancontactEnvironment environment)
    {
        var jwkSet = await _httpClientSource().GetFromJsonAsync<JwkSetJson>(BancontactEndpoints.GetJwksUri(environment)).ConfigureAwait(false);

        var keys = new Dictionary<string, ECDsa>();
        foreach (var jwk in jwkSet?.Keys ?? [])
        {
            if (jwk.Kid is null)
            {
                continue;
            }

            var key = JwkConverter.ToPublicKey(jwk);
            if (key is not null)
            {
                keys[jwk.Kid] = key;
            }
        }

        return keys;
    }

    /// <summary>The key cache and in-flight refresh of one environment.</summary>
    private sealed class EnvironmentCache
    {
        public readonly Lock Gate = new();

        public volatile Dictionary<string, ECDsa> Keys = new();

        public TaskCompletionSource<Dictionary<string, ECDsa>>? RefreshInFlight;
    }
}
