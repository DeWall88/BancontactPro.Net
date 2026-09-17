using System.Text.Json;
using BancontactPro.Models;
using BancontactPro.Signing;

namespace BancontactPro.Webhooks;

/// <summary>
/// The single entry point a webhook endpoint calls to process an inbound Bancontact callback:
/// verifies the <c>Signature</c> header (via <see cref="BancontactSignatureVerifier"/>) and, only
/// if that succeeds, deserializes the body into a <see cref="MerchantCallback"/>.
/// </summary>
public sealed class BancontactCallbackVerifier
{
    private readonly BancontactSignatureVerifier _signatureVerifier;

    /// <param name="signatureVerifier">Verifies the callback's detached JWS.</param>
    public BancontactCallbackVerifier(BancontactSignatureVerifier signatureVerifier)
    {
        _signatureVerifier = signatureVerifier;
    }

    /// <summary>
    /// Verifies and parses an inbound callback.
    /// </summary>
    /// <param name="signatureHeader">The raw <c>Signature</c> header value.</param>
    /// <param name="body">The exact raw request body bytes (must match what was signed).</param>
    /// <param name="path">The path the endpoint actually received the callback on.</param>
    /// <param name="cancellationToken">Propagated to the JWKS lookup, if a fetch is needed.</param>
    /// <exception cref="InvalidCallbackException">
    /// The signature doesn't verify, or the body doesn't match the expected schema.
    /// </exception>
    public async Task<VerifiedCallback> VerifyAndParseAsync(string signatureHeader, ReadOnlyMemory<byte> body, string path, CancellationToken cancellationToken = default)
    {
        var claims = await _signatureVerifier.VerifyAsync(signatureHeader, body, path, cancellationToken).ConfigureAwait(false);
        if (claims is null)
        {
            throw new InvalidCallbackException("The callback's signature could not be verified.");
        }

        MerchantCallback? payload;
        try
        {
            payload = JsonSerializer.Deserialize<MerchantCallback>(body.Span, BancontactJsonOptions.Default);
        }
        catch (JsonException ex)
        {
            throw new InvalidCallbackException("The callback body could not be parsed.", ex);
        }

        if (payload is null)
        {
            throw new InvalidCallbackException("The callback body deserialized to null.");
        }

        return new VerifiedCallback(payload, claims.RequestId);
    }
}
