using System.Text.Json;
using BancontactPro.Models;

namespace BancontactPro.Http;

/// <summary>Shared error-response handling for every typed Bancontact client.</summary>
public static class HttpResponseMessageExtensions
{
    /// <summary>
    /// Does nothing if <paramref name="response"/> is a success status. Otherwise, parses the
    /// body as an <see cref="ErrorResponse"/> (the shape shared by the Payment, Refund, and
    /// Reconciliation specs) and throws <see cref="BancontactApiException"/>. If the body isn't
    /// valid JSON (e.g. an upstream 5xx from infrastructure rather than the API itself), falls
    /// back to a generic exception carrying whatever raw text is available, rather than letting
    /// an unrelated <see cref="JsonException"/> mask the real HTTP error.
    /// </summary>
    public static async Task EnsureBancontactSuccessAsync(this HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        ErrorResponse? error = null;
        try
        {
            error = JsonSerializer.Deserialize<ErrorResponse>(body, BancontactJsonOptions.Default);
        }
        catch (JsonException)
        {
            // Fall through to the generic exception below.
        }

        if (error is not null)
        {
            throw new BancontactApiException(response.StatusCode, error.Code, error.Message, error.TraceId, error.SpanId);
        }

        var message = string.IsNullOrWhiteSpace(body)
            ? $"Bancontact returned {(int)response.StatusCode} with no response body."
            : $"Bancontact returned {(int)response.StatusCode}: {body}";
        throw new BancontactApiException(response.StatusCode, code: "", message, traceId: "", spanId: "");
    }
}
