using System.Net;
using BancontactPro.Models;
using BancontactPro.Reconciliation;

namespace BancontactPro.Net.Tests.Reconciliation;

public class ReconciliationClientTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public Func<HttpResponseMessage> ResponseFactory { get; set; } = () => new HttpResponseMessage(HttpStatusCode.OK);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(ResponseFactory());
        }
    }

    private static (IReconciliationClient client, FakeHandler handler) CreateClient()
    {
        var handler = new FakeHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://merchant.api.preprod.bancontact.net/") };
        return (new ReconciliationClient(httpClient), handler);
    }

    [Fact]
    public async Task GetPayoutsAsync_NoFilters_HitsPathWithoutQueryString()
    {
        var (client, handler) = CreateClient();
        const string json = """{"size":0,"totalPages":0,"totalElements":0,"number":0,"payouts":[]}""";
        handler.ResponseFactory = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };

        await client.GetPayoutsAsync();

        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal("/v3/reconciliation/payouts", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal("", handler.LastRequest.RequestUri.Query);
    }

    [Fact]
    public async Task GetPayoutsAsync_WithDateAndPaging_BuildsExpectedQueryString()
    {
        var (client, handler) = CreateClient();
        const string json = """
        {
          "size": 1,
          "totalPages": 1,
          "totalElements": 1,
          "number": 0,
          "payouts": [
            {
              "payoutId": "payout-1",
              "merchantId": "merchant-1",
              "bulkId": "NONE",
              "iban": "BE68539007547034",
              "payoutStatus": "SUCCEEDED",
              "payoutDate": "2026-01-01T00:00:00.000Z",
              "payoutCurrency": "EUR",
              "totalPayments": 5,
              "totalRefunds": 1,
              "totalPaymentAmount": 1000,
              "totalRefundAmount": 100,
              "payoutAmount": 900
            }
          ]
        }
        """;
        handler.ResponseFactory = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };

        var result = await client.GetPayoutsAsync(new DateOnly(2026, 6, 18), page: 2, size: 10000);

        Assert.Equal("?date=2026-06-18&page=2&size=10000", handler.LastRequest!.RequestUri!.Query);
        Assert.Single(result.Payouts);
        Assert.Equal(PayoutStatus.SUCCEEDED, result.Payouts[0].PayoutStatus);
        Assert.Equal(900, result.Payouts[0].PayoutAmount);
    }

    [Fact]
    public async Task GetPaymentsAsync_WithPayoutIdFilter_BuildsExpectedQueryString()
    {
        var (client, handler) = CreateClient();
        const string json = """{"size":0,"totalPages":0,"totalElements":0,"number":0,"payments":[]}""";
        handler.ResponseFactory = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };

        await client.GetPaymentsAsync(payoutId: "payout-1");

        Assert.Equal("/v3/reconciliation/payments", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Equal("?payout-id=payout-1", handler.LastRequest.RequestUri.Query);
    }

    [Fact]
    public async Task GetPaymentsAsync_WithDateRange_BuildsExpectedQueryStringAndParsesResults()
    {
        var (client, handler) = CreateClient();
        const string json = """
        {
          "size": 1,
          "totalPages": 1,
          "totalElements": 1,
          "number": 0,
          "payments": [
            {
              "paymentId": "payment-1",
              "paymentProfileId": "profile-1",
              "merchantName": "Old Skool Lan",
              "paymentChannel": "ONLINE",
              "currency": "EUR",
              "amount": 500,
              "transactionDate": "2026-06-18T10:00:00.000Z"
            }
          ]
        }
        """;
        handler.ResponseFactory = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };

        var result = await client.GetPaymentsAsync(startDate: new DateOnly(2026, 6, 1), endDate: new DateOnly(2026, 6, 18));

        Assert.Equal("?start-date=2026-06-01&end-date=2026-06-18", handler.LastRequest!.RequestUri!.Query);
        Assert.Equal(PaymentChannel.ONLINE, result.Payments[0].PaymentChannel);
        Assert.Null(result.Payments[0].PayoutId);
    }

    [Fact]
    public async Task GetRefundsAsync_ParsesRefundIdField()
    {
        var (client, handler) = CreateClient();
        const string json = """
        {
          "size": 1,
          "totalPages": 1,
          "totalElements": 1,
          "number": 0,
          "refunds": [
            {
              "refundId": "refund-1",
              "paymentId": "payment-1",
              "paymentProfileId": "profile-1",
              "merchantName": "Old Skool Lan",
              "paymentChannel": "ONLINE",
              "currency": "EUR",
              "amount": 100,
              "transactionDate": "2026-06-18T10:00:00.000Z"
            }
          ]
        }
        """;
        handler.ResponseFactory = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };

        var result = await client.GetRefundsAsync(payoutId: "payout-1");

        Assert.Equal("/v3/reconciliation/refunds", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Equal("refund-1", result.Refunds[0].RefundId);
    }
}
