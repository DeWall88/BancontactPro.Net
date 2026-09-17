using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace BancontactPro.Signing;

/// <summary>
/// Fetches and caches Bancontact's JWKS. On a cache miss, refreshes once (to pick up key
/// rotation) before concluding a <c>kid</c> genuinely doesn't exist. Concurrent misses are
/// coalesced into a single in-flight fetch.
/// </summary>
public sealed class JwksClient : IJwksClient
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<JwksClientOptions> _options;

    private readonly Lock _refreshGate = new();
    private Dictionary<string, ECDsa> _cache = new();
    private TaskCompletionSource<Dictionary<string, ECDsa>>? _refreshInFlight;

    /// <param name="httpClient">The client used to fetch the JWKS document.</param>
    /// <param name="options">Configuration specifying which JWKS endpoint to fetch from.</param>
    public JwksClient(HttpClient httpClient, IOptions<JwksClientOptions> options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<ECDsa?> GetKeyAsync(string kid, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(kid, out var key))
        {
            return key;
        }

        var refreshed = await RefreshAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        return refreshed.TryGetValue(kid, out key) ? key : null;
    }

    private Task<Dictionary<string, ECDsa>> RefreshAsync()
    {
        // Registers the in-flight marker *before* the fetch starts, rather than having the fetch
        // clear its own marker on completion: if the fetch completes synchronously (e.g. behind a
        // fake handler in tests, or simply a fast response), a self-clearing approach can run its
        // "clear" step before this method has even stored the marker, permanently wedging a stale
        // completed result in place. Setting it up-front removes that ordering hazard entirely.
        TaskCompletionSource<Dictionary<string, ECDsa>> tcs;
        lock (_refreshGate)
        {
            if (_refreshInFlight is { } inFlight)
            {
                return inFlight.Task;
            }

            tcs = _refreshInFlight = new TaskCompletionSource<Dictionary<string, ECDsa>>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        _ = FetchAndCompleteAsync(tcs);
        return tcs.Task;
    }

    private async Task FetchAndCompleteAsync(TaskCompletionSource<Dictionary<string, ECDsa>> tcs)
    {
        try
        {
            var cache = await RefreshCoreAsync().ConfigureAwait(false);
            lock (_refreshGate)
            {
                _refreshInFlight = null;
            }

            tcs.SetResult(cache);
        }
        catch (Exception ex)
        {
            lock (_refreshGate)
            {
                _refreshInFlight = null;
            }

            tcs.SetException(ex);
        }
    }

    private async Task<Dictionary<string, ECDsa>> RefreshCoreAsync()
    {
        var jwkSet = await _httpClient.GetFromJsonAsync<JwkSetJson>(_options.Value.JwksUri).ConfigureAwait(false);

        var cache = new Dictionary<string, ECDsa>();
        foreach (var jwk in jwkSet?.Keys ?? [])
        {
            if (jwk.Kid is null)
            {
                continue;
            }

            var key = JwkConverter.ToPublicKey(jwk);
            if (key is not null)
            {
                cache[jwk.Kid] = key;
            }
        }

        _cache = cache;
        return cache;
    }
}
