using System.Net.Http.Json;
using BancontactPro.Http;
using BancontactPro.Models;

namespace BancontactPro.Payments;

/// <summary>
/// Default <see cref="IPaymentClient"/> implementation. Constructed from a plain
/// <see cref="HttpClient"/> with its <see cref="HttpClient.BaseAddress"/> pointed at the
/// Bancontact Payment API — signing is attached upstream via
/// <c>BancontactSigningHandler</c> (see #2), so this type issues ordinary relative-path
/// requests and doesn't know anything about JWS itself.
/// </summary>
public sealed class PaymentClient : IPaymentClient
{
    private readonly HttpClient _httpClient;

    /// <param name="httpClient">An <see cref="HttpClient"/> configured for the Payment API, with signing already attached.</param>
    public PaymentClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<CreatePaymentResponse> CreateAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("v3/payments", request, BancontactJsonOptions.Default, cancellationToken).ConfigureAwait(false);
        await response.EnsureBancontactSuccessAsync(cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<CreatePaymentResponse>(BancontactJsonOptions.Default, cancellationToken).ConfigureAwait(false))!;
    }

    /// <inheritdoc />
    public async Task<Payment> GetAsync(string paymentId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"v3/payments/{Uri.EscapeDataString(paymentId)}", cancellationToken).ConfigureAwait(false);
        await response.EnsureBancontactSuccessAsync(cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<Payment>(BancontactJsonOptions.Default, cancellationToken).ConfigureAwait(false))!;
    }

    /// <inheritdoc />
    public async Task CancelAsync(string paymentId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.DeleteAsync($"v3/payments/{Uri.EscapeDataString(paymentId)}", cancellationToken).ConfigureAwait(false);
        await response.EnsureBancontactSuccessAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PaymentSearchResponse> SearchAsync(PaymentSearchQuery query, int? page = null, int? size = null, CancellationToken cancellationToken = default)
    {
        var queryParams = new List<string>();
        if (page is not null)
        {
            queryParams.Add($"page={page}");
        }

        if (size is not null)
        {
            queryParams.Add($"size={size}");
        }

        var relativeUri = queryParams.Count > 0 ? $"v3/payments/search?{string.Join('&', queryParams)}" : "v3/payments/search";

        using var response = await _httpClient.PostAsJsonAsync(relativeUri, query, BancontactJsonOptions.Default, cancellationToken).ConfigureAwait(false);
        await response.EnsureBancontactSuccessAsync(cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<PaymentSearchResponse>(BancontactJsonOptions.Default, cancellationToken).ConfigureAwait(false))!;
    }

    /// <inheritdoc />
    public async Task AcknowledgeAsync(string paymentId, PaymentAcknowledgeRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync($"v3/payments/{Uri.EscapeDataString(paymentId)}/acknowledge", request, BancontactJsonOptions.Default, cancellationToken).ConfigureAwait(false);
        await response.EnsureBancontactSuccessAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<DebtorRefundIban> GetDebtorRefundIbanAsync(string paymentId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"v3/payments/{Uri.EscapeDataString(paymentId)}/debtor/refundIban", cancellationToken).ConfigureAwait(false);
        await response.EnsureBancontactSuccessAsync(cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<DebtorRefundIban>(BancontactJsonOptions.Default, cancellationToken).ConfigureAwait(false))!;
    }

    /// <inheritdoc />
    public async Task<CreatePaymentResponse> CreateStaticQrPaymentAsync(CreateStaticQrPaymentRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("v3/payments/pos", request, BancontactJsonOptions.Default, cancellationToken).ConfigureAwait(false);
        await response.EnsureBancontactSuccessAsync(cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<CreatePaymentResponse>(BancontactJsonOptions.Default, cancellationToken).ConfigureAwait(false))!;
    }
}
