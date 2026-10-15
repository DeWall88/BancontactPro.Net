using System.Net.Http.Headers;

namespace BancontactPro.Http;

/// <summary>
/// Attaches the bearer API key to every outgoing request, without a JWS signature. Used for the
/// Payment API: its operations list the API key and the JWS signature as alternative security
/// requirements (<c>security: [{api_key_payment_profile}, {JWS-Request-Signature-Payment}]</c>),
/// so the API key alone authenticates them (see docs/research-notes.md, "Resolved: which calls
/// need a signature"). Refund and Reconciliation use <see cref="Signing.BancontactSigningHandler"/>.
/// </summary>
public sealed class BancontactApiKeyHandler : DelegatingHandler
{
    private readonly string _apiKey;

    /// <param name="apiKey">The bearer API key issued for the merchant profile being called.</param>
    public BancontactApiKeyHandler(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        _apiKey = apiKey;
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        return base.SendAsync(request, cancellationToken);
    }
}
