using System.Text.Json;
using System.Text.Json.Serialization;

namespace BancontactPro;

/// <summary>
/// The <see cref="JsonSerializerOptions"/> shared by every DTO in this library — camelCase
/// property names and string-valued enums, matching Bancontact Pro's wire format. Internal: an
/// implementation detail of this library's own (de)serialization, not something consumers need.
/// </summary>
internal static class BancontactJsonOptions
{
    /// <summary>The shared, immutable options instance.</summary>
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };
}
