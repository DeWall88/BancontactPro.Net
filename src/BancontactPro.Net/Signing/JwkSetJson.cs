using System.Text.Json.Serialization;

namespace BancontactPro.Signing;

/// <summary>Wire shape of a JWK Set document (RFC 7517 §5).</summary>
internal sealed class JwkSetJson
{
    [JsonPropertyName("keys")]
    public JwkJson[] Keys { get; init; } = [];
}

/// <summary>Wire shape of a single JWK entry within a JWK Set.</summary>
internal sealed class JwkJson
{
    [JsonPropertyName("kty")]
    public string? Kty { get; init; }

    [JsonPropertyName("crv")]
    public string? Crv { get; init; }

    [JsonPropertyName("kid")]
    public string? Kid { get; init; }

    [JsonPropertyName("x")]
    public string? X { get; init; }

    [JsonPropertyName("y")]
    public string? Y { get; init; }
}
