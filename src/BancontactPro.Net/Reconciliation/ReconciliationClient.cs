using System.Net.Http.Json;
using BancontactPro.Http;
using BancontactPro.Models;

namespace BancontactPro.Reconciliation;

/// <summary>
/// Default <see cref="IReconciliationClient"/> implementation. Constructed from a plain
/// <see cref="HttpClient"/> with its <see cref="HttpClient.BaseAddress"/> pointed at the
/// Bancontact API host — signing is attached upstream via <c>BancontactSigningHandler</c> (#2).
/// </summary>
public sealed class ReconciliationClient : IReconciliationClient
{
    private readonly HttpClient _httpClient;

    /// <param name="httpClient">An <see cref="HttpClient"/> configured for the Reconciliation API, with signing already attached.</param>
    public ReconciliationClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<PayoutListResponse> GetPayoutsAsync(DateOnly? date = null, int? page = null, int? size = null, CancellationToken cancellationToken = default)
    {
        var queryParams = new List<string>();
        if (date is not null)
        {
            queryParams.Add($"date={date.Value:yyyy-MM-dd}");
        }

        AddPaging(queryParams, page, size);

        using var response = await _httpClient.GetAsync(BuildUri("v3/reconciliation/payouts", queryParams), cancellationToken).ConfigureAwait(false);
        await response.EnsureBancontactSuccessAsync(cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<PayoutListResponse>(BancontactJsonOptions.Default, cancellationToken).ConfigureAwait(false))!;
    }

    /// <inheritdoc />
    public async Task<PayoutPaymentsResponse> GetPaymentsAsync(string? payoutId = null, DateOnly? startDate = null, DateOnly? endDate = null, int? page = null, int? size = null, CancellationToken cancellationToken = default)
    {
        var queryParams = new List<string>();
        AddPayoutFilter(queryParams, payoutId, startDate, endDate);
        AddPaging(queryParams, page, size);

        using var response = await _httpClient.GetAsync(BuildUri("v3/reconciliation/payments", queryParams), cancellationToken).ConfigureAwait(false);
        await response.EnsureBancontactSuccessAsync(cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<PayoutPaymentsResponse>(BancontactJsonOptions.Default, cancellationToken).ConfigureAwait(false))!;
    }

    /// <inheritdoc />
    public async Task<PayoutRefundsResponse> GetRefundsAsync(string? payoutId = null, DateOnly? startDate = null, DateOnly? endDate = null, int? page = null, int? size = null, CancellationToken cancellationToken = default)
    {
        var queryParams = new List<string>();
        AddPayoutFilter(queryParams, payoutId, startDate, endDate);
        AddPaging(queryParams, page, size);

        using var response = await _httpClient.GetAsync(BuildUri("v3/reconciliation/refunds", queryParams), cancellationToken).ConfigureAwait(false);
        await response.EnsureBancontactSuccessAsync(cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<PayoutRefundsResponse>(BancontactJsonOptions.Default, cancellationToken).ConfigureAwait(false))!;
    }

    private static void AddPayoutFilter(List<string> queryParams, string? payoutId, DateOnly? startDate, DateOnly? endDate)
    {
        if (payoutId is not null)
        {
            queryParams.Add($"payout-id={Uri.EscapeDataString(payoutId)}");
        }

        if (startDate is not null)
        {
            queryParams.Add($"start-date={startDate.Value:yyyy-MM-dd}");
        }

        if (endDate is not null)
        {
            queryParams.Add($"end-date={endDate.Value:yyyy-MM-dd}");
        }
    }

    private static void AddPaging(List<string> queryParams, int? page, int? size)
    {
        if (page is not null)
        {
            queryParams.Add($"page={page}");
        }

        if (size is not null)
        {
            queryParams.Add($"size={size}");
        }
    }

    private static string BuildUri(string path, List<string> queryParams) =>
        queryParams.Count > 0 ? $"{path}?{string.Join('&', queryParams)}" : path;
}
