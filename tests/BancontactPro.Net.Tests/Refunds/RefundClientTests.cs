using System.Net;
using BancontactPro.Http;
using BancontactPro.Models;
using BancontactPro.Refunds;

namespace BancontactPro.Net.Tests.Refunds;

public class RefundClientTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }
        public Func<HttpResponseMessage> ResponseFactory { get; set; } = () => new HttpResponseMessage(HttpStatusCode.OK);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content is not null ? await request.Content.ReadAsStringAsync(cancellationToken) : null;
            return ResponseFactory();
        }
    }

    private static (IRefundClient client, FakeHandler handler) CreateClient()
    {
        var handler = new FakeHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://merchant.api.preprod.bancontact.net/") };
        return (new RefundClient(httpClient), handler);
    }

    private const string SampleCreationResponseJson = """
    {
      "refundId": "refund-1",
      "paymentId": "payment-1",
      "status": "PENDING",
      "amount": 500,
      "currency": "EUR",
      "creationDate": "2026-01-02T03:04:05.000Z"
    }
    """;

    [Fact]
    public async Task CreateAsync_PostsToCorrectPathWithIdempotencyKeyHeader()
    {
        var (client, handler) = CreateClient();
        handler.ResponseFactory = () => new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent(SampleCreationResponseJson) };

        var result = await client.CreateAsync("payment-1", new CreateRefundRequest(500, "EUR"), "idem-key-1");

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/v3/payments/payment-1/refunds", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal("idem-key-1", Assert.Single(handler.LastRequest.Headers.GetValues("Idempotency-Key")));
        Assert.Contains("\"amount\":500", handler.LastRequestBody);
        Assert.Equal(RefundStatus.PENDING, result.Status);
        Assert.Equal("refund-1", result.RefundId);
    }

    [Fact]
    public async Task GetAsync_GetsCorrectPathAndParsesFullStatusEnum()
    {
        var (client, handler) = CreateClient();
        const string json = """
        {
          "refundId": "refund-1",
          "paymentId": "payment-1",
          "amount": 500,
          "currency": "EUR",
          "status": "REFUNDED",
          "creationDate": "2026-01-02T03:04:05.000Z"
        }
        """;
        handler.ResponseFactory = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };

        var result = await client.GetAsync("payment-1", "refund-1");

        Assert.Equal("/v3/payments/payment-1/refunds/refund-1", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Equal(RefundStatus.REFUNDED, result.Status);
    }

    [Fact]
    public async Task CreateAsync_RefundRequestConflict_ThrowsIdempotencyConflictException()
    {
        var (client, handler) = CreateClient();
        handler.ResponseFactory = () => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("""{"code":"REFUND_REQUEST_CONFLICT","message":"key reused"}"""),
        };

        var ex = await Assert.ThrowsAsync<RefundIdempotencyConflictException>(
            () => client.CreateAsync("payment-1", new CreateRefundRequest(500, "EUR"), "idem-key-1"));

        Assert.Equal("idem-key-1", ex.IdempotencyKey);
        Assert.IsType<BancontactApiException>(ex.InnerException);
    }

    [Fact]
    public async Task CreateAsync_OtherError_ThrowsPlainBancontactApiException()
    {
        var (client, handler) = CreateClient();
        handler.ResponseFactory = () => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("""{"code":"INVALID_REFUND_AMOUNT","message":"too high"}"""),
        };

        var ex = await Assert.ThrowsAsync<BancontactApiException>(
            () => client.CreateAsync("payment-1", new CreateRefundRequest(500, "EUR"), "idem-key-1"));

        Assert.Equal("INVALID_REFUND_AMOUNT", ex.Code);
    }
}
