using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BancontactPro.Signing;

/// <summary>
/// Exports the public half of an ES256 (P-256) key pair as a JWK, so a consuming application can
/// publish it at whatever URL it registers as its JWKS endpoint during Bancontact onboarding.
/// This library does not host that endpoint itself — that's an app-level HTTP concern.
/// </summary>
public static class JwkExporter
{
    private sealed class PublicJwk
    {
        [JsonPropertyName("kty")]
        public string Kty { get; init; } = "EC";

        [JsonPropertyName("crv")]
        public string Crv { get; init; } = "P-256";

        [JsonPropertyName("kid")]
        public required string Kid { get; init; }

        [JsonPropertyName("use")]
        public string Use { get; init; } = "sig";

        [JsonPropertyName("alg")]
        public string Alg { get; init; } = "ES256";

        [JsonPropertyName("x")]
        public required string X { get; init; }

        [JsonPropertyName("y")]
        public required string Y { get; init; }
    }

    /// <summary>
    /// Builds the public JWK JSON for <paramref name="key"/>, keyed under <paramref name="kid"/>.
    /// </summary>
    /// <param name="key">A P-256 key (private or public half — only the public point is used).</param>
    /// <param name="kid">The key id to embed, matching what's registered with Bancontact.</param>
    public static string ToPublicJwkJson(ECDsa key, string kid)
    {
        var parameters = key.ExportParameters(includePrivateParameters: false);

        if (!IsP256(parameters.Curve))
        {
            throw new NotSupportedException(
                $"Only P-256 (ES256) keys are supported; got curve '{parameters.Curve.Oid?.FriendlyName ?? parameters.Curve.Oid?.Value}'.");
        }

        var jwk = new PublicJwk
        {
            Kid = kid,
            X = Base64Url.EncodeToString(parameters.Q.X!),
            Y = Base64Url.EncodeToString(parameters.Q.Y!),
        };

        return JsonSerializer.Serialize(jwk);
    }

    /// <summary>The OID of NIST P-256 (secp256r1 / prime256v1).</summary>
    private const string P256Oid = "1.2.840.10045.3.1.7";

    /// <summary>
    /// Whether <paramref name="curve"/> is P-256. The OID is the reliable check: the friendly name
    /// depends on the platform's crypto library (<c>nistP256</c> on Windows, <c>ECDSA_P256</c> with
    /// OpenSSL on Linux), so names are only a fallback for when no OID value is reported.
    /// </summary>
    internal static bool IsP256(ECCurve curve) =>
        curve.IsNamed
        && (curve.Oid.Value == P256Oid
            || curve.Oid.FriendlyName is "nistP256" or "ECDSA_P256" or "secp256r1" or "prime256v1");
}
