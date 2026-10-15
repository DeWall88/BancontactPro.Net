using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BancontactPro.Signing;

/// <summary>
/// Verifies a detached JWS Bancontact attaches to inbound callbacks, resolving the signing key
/// from a JWKS (<see cref="IJwksClient"/>) rather than a locally-held private key.
/// </summary>
public sealed class BancontactSignatureVerifier
{
    private static readonly HashSet<string> ExpectedCrit =
    [
        "https://payconiq.com/sub",
        "https://payconiq.com/iss",
        "https://payconiq.com/iat",
        "https://payconiq.com/jti",
        "https://payconiq.com/path",
    ];

    private readonly IJwksClient _jwksClient;

    /// <param name="jwksClient">Resolves Bancontact's public keys by <c>kid</c>.</param>
    public BancontactSignatureVerifier(IJwksClient jwksClient)
    {
        _jwksClient = jwksClient;
    }

    /// <summary>
    /// Verifies <paramref name="signatureHeader"/> (the raw <c>Signature</c> header value) against
    /// <paramref name="body"/> and <paramref name="path"/> (the path the endpoint actually
    /// received the callback on). Returns the parsed claims on success, or <c>null</c> if the
    /// value is malformed, the signature doesn't validate, the signing key can't be resolved, an
    /// unrecognized critical extension is present, or the signed <c>path</c> claim doesn't match
    /// <paramref name="path"/>.
    /// </summary>
    public async Task<JoseHeader?> VerifyAsync(string signatureHeader, ReadOnlyMemory<byte> body, string path, CancellationToken cancellationToken = default)
    {
        if (!DetachedJwsSigner.TrySplit(signatureHeader, out var encodedHeader, out var encodedSignature))
        {
            return null;
        }

        JoseHeaderJson headerJson;
        try
        {
            var headerBytes = Base64Url.DecodeFromChars(encodedHeader);
            headerJson = JsonSerializer.Deserialize<JoseHeaderJson>(headerBytes)
                ?? throw new JsonException("JOSE header deserialized to null.");
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            return null;
        }

        if (headerJson.Crit.Length != ExpectedCrit.Count || !headerJson.Crit.All(ExpectedCrit.Contains))
        {
            return null;
        }

        if (headerJson.Path != path)
        {
            return null;
        }

        byte[] signature;
        try
        {
            signature = Base64Url.DecodeFromChars(encodedSignature);
        }
        catch (FormatException)
        {
            return null;
        }

        var publicKey = await _jwksClient.GetKeyAsync(headerJson.Kid, cancellationToken).ConfigureAwait(false);
        if (publicKey is null)
        {
            return null;
        }

        var signingInput = DetachedJwsSigner.ComputeSigningInput(encodedHeader, body.Span);
        if (!publicKey.VerifyData(signingInput, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
        {
            return null;
        }

        if (!DateTimeOffset.TryParseExact(headerJson.Iat, "yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var issuedAt))
        {
            return null;
        }

        return new JoseHeader(
            Kid: headerJson.Kid,
            MerchantProfileId: headerJson.Sub,
            Issuer: headerJson.Iss,
            IssuedAt: issuedAt,
            RequestId: headerJson.Jti,
            Path: headerJson.Path);
    }
}
