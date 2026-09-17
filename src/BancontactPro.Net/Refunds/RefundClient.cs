using System.Net.Http.Json;
using BancontactPro.Http;
using BancontactPro.Models;

namespace BancontactPro.Refunds;

/// <summary>
/// Default <see cref="IRefundClient"/> implementation. Constructed from a plain
/// <see cref="HttpClient"/> with its <see cref="HttpClient.BaseAddress"/> pointed at the
/// Bancontact Payment API host (refunds are scoped under the payment they refund, not a
/// top-level resource) — signing is attached upstream via <c>BancontactSigningHandler</c> (#2).
/// </summary>
public sealed class RefundClient : IRefundClient
{
    private readonly HttpClient _httpClient;

    /// <param name="httpClient">An <see cref="HttpClient"/> configured for the Payment API, with signing already attached.</param>
    public RefundClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<RefundCreationResponse> CreateAsync(string paymentId, CreateRefundRequest request, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"v3/payments/{Uri.EscapeDataString(paymentId)}/refunds")
        {
            Content = JsonContent.Create(request, options: BancontactJsonOptions.Default),
        };
        httpRequest.Headers.Add("Idempotency-Key", idempotencyKey);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);

        try
        {
            await response.EnsureBancontactSuccessAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (BancontactApiException ex) when (ex.Code == "REFUND_REQUEST_CONFLICT")
        {
            throw new RefundIdempotencyConflictException(idempotencyKey, ex);
        }

        return (await response.Content.ReadFromJsonAsync<RefundCreationResponse>(BancontactJsonOptions.Default, cancellationToken).ConfigureAwait(false))!;
    }

    /// <inheritdoc />
    public async Task<RefundModel> GetAsync(string paymentId, string refundId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"v3/payments/{Uri.EscapeDataString(paymentId)}/refunds/{Uri.EscapeDataString(refundId)}", cancellationToken).ConfigureAwait(false);
        await response.EnsureBancontactSuccessAsync(cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<RefundModel>(BancontactJsonOptions.Default, cancellationToken).ConfigureAwait(false))!;
    }
}
