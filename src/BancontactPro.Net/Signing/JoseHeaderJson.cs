using System.Text.Json.Serialization;

namespace BancontactPro.Signing;

/// <summary>
/// Wire shape of the JOSE header, matching the exact (namespaced) claim names Bancontact Pro
/// requires. Internal — <see cref="JoseHeader"/> is the public-facing type.
/// </summary>
internal sealed class JoseHeaderJson
{
    [JsonPropertyName("typ")]
    public string Typ { get; init; } = "jose+json";

    [JsonPropertyName("kid")]
    public required string Kid { get; init; }

    [JsonPropertyName("alg")]
    public string Alg { get; init; } = "ES256";

    [JsonPropertyName("https://payconiq.com/sub")]
    public required string Sub { get; init; }

    [JsonPropertyName("https://payconiq.com/iss")]
    public required string Iss { get; init; }

    [JsonPropertyName("https://payconiq.com/iat")]
    public required string Iat { get; init; }

    [JsonPropertyName("https://payconiq.com/jti")]
    public required string Jti { get; init; }

    [JsonPropertyName("https://payconiq.com/path")]
    public required string Path { get; init; }

    [JsonPropertyName("crit")]
    public string[] Crit { get; init; } =
    [
        "https://payconiq.com/sub",
        "https://payconiq.com/iss",
        "https://payconiq.com/iat",
        "https://payconiq.com/jti",
        "https://payconiq.com/path",
    ];
}
