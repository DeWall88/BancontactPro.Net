using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace BancontactPro.Signing;

/// <summary>
/// Signs every outgoing request with Bancontact Pro's detached JWS scheme and attaches the
/// bearer API key, per docs/research-notes.md ("Resolved: request signing header"). Install as a
/// handler on the <see cref="HttpClient"/> used to call the Payment/Refund/Reconciliation APIs.
/// </summary>
public sealed class BancontactSigningHandler : DelegatingHandler
{
    private readonly IRequestSigningKeyProvider _keyProvider;
    private readonly IOptions<BancontactSigningOptions> _options;

    /// <param name="keyProvider">Supplies the merchant's private key and registered kid.</param>
    /// <param name="options">Per-merchant signing configuration (API key, profile id, issuer).</param>
    public BancontactSigningHandler(IRequestSigningKeyProvider keyProvider, IOptions<BancontactSigningOptions> options)
    {
        _keyProvider = keyProvider;
        _options = options;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is not null
            ? await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false)
            : [];

        var options = _options.Value;
        var header = new JoseHeader(
            Kid: _keyProvider.Kid,
            MerchantProfileId: options.MerchantProfileId,
            Issuer: options.Issuer,
            IssuedAt: DateTimeOffset.UtcNow,
            RequestId: Guid.NewGuid().ToString(),
            Path: request.RequestUri!.AbsolutePath);

        var signature = DetachedJwsSigner.Sign(header, body, _keyProvider.PrivateKey);

        request.Headers.Remove("Signature");
        request.Headers.TryAddWithoutValidation("Signature", signature);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
