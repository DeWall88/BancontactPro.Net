using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BancontactPro.Signing;

/// <summary>
/// Computes Bancontact Pro's detached JWS request signature (RFC 7797, ES256).
/// </summary>
/// <remarks>
/// Per docs/research-notes.md, the signature value is <c>base64url(header) + "." + signature</c>,
/// where <c>signature = ES256(base64url(header) + "." + base64url(body))</c> — a genuinely
/// two-segment value, not the usual three-part compact JWS with an empty payload segment.
/// </remarks>
public static class DetachedJwsSigner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // The header carries literal URI-shaped claim names (e.g. "https://payconiq.com/sub");
        // they must round-trip byte-for-byte, not get escaped.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Builds the JOSE header and signs <paramref name="body"/> with <paramref name="privateKey"/>,
    /// returning the two-segment <c>base64url(header).base64url(signature)</c> value for the
    /// <c>Signature</c> header.
    /// </summary>
    /// <param name="header">The claims to embed in the JOSE header.</param>
    /// <param name="body">
    /// The exact request (or response) bytes being signed. Empty for bodyless requests
    /// (GET/DELETE) — never omit this parameter in that case, pass an empty span instead.
    /// </param>
    /// <param name="privateKey">An ES256 (P-256) private key.</param>
    public static string Sign(JoseHeader header, ReadOnlySpan<byte> body, ECDsa privateKey)
    {
        var headerJson = new JoseHeaderJson
        {
            Kid = header.Kid,
            Sub = header.MerchantProfileId,
            Iss = header.Issuer,
            Iat = header.IssuedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
            Jti = header.RequestId,
            Path = header.Path,
        };

        var headerBytes = JsonSerializer.SerializeToUtf8Bytes(headerJson, JsonOptions);

        var encodedHeader = Base64Url.EncodeToString(headerBytes);
        var encodedBody = Base64Url.EncodeToString(body);

        var signingInput = Encoding.ASCII.GetBytes($"{encodedHeader}.{encodedBody}");
        var signature = privateKey.SignData(signingInput, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return $"{encodedHeader}.{Base64Url.EncodeToString(signature)}";
    }

    /// <summary>
    /// Verifies a value produced by <see cref="Sign"/> against the signer's public key. Exposed
    /// primarily for testing the signer itself; verifying inbound Bancontact-signed callbacks is
    /// a separate concern (different key, different header claim expectations).
    /// </summary>
    public static bool Verify(string detachedJws, ReadOnlySpan<byte> body, ECDsa publicKey)
    {
        var dot = detachedJws.IndexOf('.');
        if (dot < 0 || detachedJws.IndexOf('.', dot + 1) >= 0)
        {
            return false;
        }

        var encodedHeader = detachedJws[..dot];
        var encodedSignature = detachedJws[(dot + 1)..];

        var encodedBody = Base64Url.EncodeToString(body);
        var signingInput = Encoding.ASCII.GetBytes($"{encodedHeader}.{encodedBody}");

        byte[] signature;
        try
        {
            signature = Base64Url.DecodeFromChars(encodedSignature);
        }
        catch (FormatException)
        {
            return false;
        }

        return publicKey.VerifyData(signingInput, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }
}
