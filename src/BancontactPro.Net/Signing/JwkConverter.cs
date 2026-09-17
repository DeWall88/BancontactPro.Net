using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;

namespace BancontactPro.Signing;

/// <summary>
/// Imports a JWK's public key material into an <see cref="ECDsa"/> instance, the inverse of
/// <see cref="JwkExporter.ToPublicJwkJson"/>.
/// </summary>
public static class JwkConverter
{
    /// <summary>
    /// Converts a single JWK JSON document into a P-256 public key. Returns <c>null</c> for any
    /// key that isn't an EC/P-256 key rather than throwing — a JWK Set may legitimately contain
    /// key types this library doesn't use, and callers should skip those, not fail.
    /// </summary>
    public static ECDsa? ToPublicKey(string jwkJson) => ToPublicKey(JsonSerializer.Deserialize<JwkJson>(jwkJson)!);

    /// <summary>Converts an already-parsed JWK entry. See <see cref="ToPublicKey(string)"/>.</summary>
    internal static ECDsa? ToPublicKey(JwkJson jwk)
    {
        if (jwk.Kty != "EC" || jwk.Crv != "P-256" || jwk.X is null || jwk.Y is null)
        {
            return null;
        }

        var parameters = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = Base64Url.DecodeFromChars(jwk.X),
                Y = Base64Url.DecodeFromChars(jwk.Y),
            },
        };

        var key = ECDsa.Create();
        key.ImportParameters(parameters);
        return key;
    }
}
