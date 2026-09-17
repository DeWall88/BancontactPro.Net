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

        if (parameters.Curve.Oid.FriendlyName != ECCurve.NamedCurves.nistP256.Oid.FriendlyName)
        {
            throw new NotSupportedException(
                $"Only P-256 (ES256) keys are supported; got curve '{parameters.Curve.Oid.FriendlyName}'.");
        }

        var jwk = new PublicJwk
        {
            Kid = kid,
            X = Base64Url.EncodeToString(parameters.Q.X!),
            Y = Base64Url.EncodeToString(parameters.Q.Y!),
        };

        return JsonSerializer.Serialize(jwk);
    }
}
