using System.Text.Json;
using System.Text.Json.Serialization;

namespace BancontactPro;

/// <summary>
/// The <see cref="JsonSerializerOptions"/> shared by every DTO in this library — camelCase
/// property names and string-valued enums, matching Bancontact Pro's wire format. Internal: an
/// implementation detail of this library's own (de)serialization, not something consumers need.
/// </summary>
/// <remarks>
/// Unset optional fields are <b>omitted</b> from requests, never sent as <c>null</c>: the spec
/// defines them as optional, not nullable (<c>currency</c> is an enum with only <c>EUR</c>), and
/// omitted URLs fall back to the product's defaults. An explicit <c>null</c> can be rejected.
/// </remarks>
internal static class BancontactJsonOptions
{
    /// <summary>The shared, immutable options instance.</summary>
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };
}
