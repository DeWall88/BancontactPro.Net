using System.Net;
using System.Net.Http.Json;
using BancontactPro.Http;
using BancontactPro.Models;
using BancontactPro.Payments;

namespace BancontactPro.Net.Tests.Payments;

public class PaymentClientTests
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

    private static (IPaymentClient client, FakeHandler handler) CreateClient()
    {
        var handler = new FakeHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://merchant.api.preprod.bancontact.net/") };
        return (new PaymentClient(httpClient), handler);
    }

    private const string SamplePaymentJson = """
    {
      "paymentId": "5f91483d-78a7-4914-bc6f=",
      "createdAt": "2026-01-02T03:04:05.000Z",
      "expireAt": "2026-01-02T03:19:05.000Z",
      "currency": "EUR",
      "status": "SUCCEEDED",
      "creditor": {},
      "amount": 1000,
      "_links": {
        "self": { "href": "https://api/self" },
        "deeplink": { "href": "https://payconiq.com/pay/2/x" },
        "qrcode": { "href": "https://qr/x" }
      }
    }
    """;

    private const string SampleCreateResponseJson = """
    {
      "paymentId": "5f91483d-78a7-4914-bc6f=",
      "status": "PENDING",
      "createdAt": "2026-01-02T03:04:05.000Z",
      "expiresAt": "2026-01-02T03:19:05.000Z",
      "amount": 1000,
      "currency": "EUR",
      "creditor": {},
      "_links": {
        "self": { "href": "https://api/self" },
        "deeplink": { "href": "https://payconiq.com/pay/2/x" },
        "qrcode": { "href": "https://qr/x" }
      }
    }
    """;

    [Fact]
    public async Task CreateAsync_PostsToCorrectPathAndParsesResponse()
    {
        var (client, handler) = CreateClient();
        handler.ResponseFactory = () => new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent(SampleCreateResponseJson) };

        var result = await client.CreateAsync(new CreatePaymentRequest(Amount: 1000));

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/v3/payments", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"amount\":1000", handler.LastRequestBody);
        Assert.Equal("5f91483d-78a7-4914-bc6f=", result.PaymentId);
    }

    [Fact]
    public async Task GetAsync_GetsCorrectPathAndParsesResponse()
    {
        var (client, handler) = CreateClient();
        handler.ResponseFactory = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SamplePaymentJson) };

        var result = await client.GetAsync("5f91483d-78a7-4914-bc6f=");

        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal("/v3/payments/5f91483d-78a7-4914-bc6f%3D", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal(MerchantPaymentStatus.SUCCEEDED, result.Status);
    }

    [Fact]
    public async Task CancelAsync_SendsDeleteToCorrectPath()
    {
        var (client, handler) = CreateClient();
        handler.ResponseFactory = () => new HttpResponseMessage(HttpStatusCode.NoContent);

        await client.CancelAsync("payment-1");

        Assert.Equal(HttpMethod.Delete, handler.LastRequest!.Method);
        Assert.Equal("/v3/payments/payment-1", handler.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task SearchAsync_PostsBodyAndIncludesPageSizeQueryParams()
    {
        var (client, handler) = CreateClient();
        const string searchResponseJson = """{"size":0,"totalPages":0,"totalElements":0,"number":0,"details":[]}""";
        handler.ResponseFactory = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(searchResponseJson) };

        await client.SearchAsync(new PaymentSearchQuery(Reference: "ref-1"), page: 2, size: 25);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/v3/payments/search", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal("?page=2&size=25", handler.LastRequest.RequestUri.Query);
        Assert.Contains("\"reference\":\"ref-1\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task SearchAsync_WithoutPaging_OmitsQueryString()
    {
        var (client, handler) = CreateClient();
        const string searchResponseJson = """{"size":0,"totalPages":0,"totalElements":0,"number":0,"details":[]}""";
        handler.ResponseFactory = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(searchResponseJson) };

        await client.SearchAsync(new PaymentSearchQuery());

        Assert.Equal("", handler.LastRequest!.RequestUri!.Query);
    }

    [Fact]
    public async Task AcknowledgeAsync_PostsToCorrectPath()
    {
        var (client, handler) = CreateClient();
        handler.ResponseFactory = () => new HttpResponseMessage(HttpStatusCode.Accepted);

        await client.AcknowledgeAsync("payment-1", new PaymentAcknowledgeRequest("EUR", 500));

        Assert.Equal("/v3/payments/payment-1/acknowledge", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("\"amount\":500", handler.LastRequestBody);
    }

    [Fact]
    public async Task GetDebtorRefundIbanAsync_GetsCorrectPathAndParsesResponse()
    {
        var (client, handler) = CreateClient();
        handler.ResponseFactory = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"iban":"BE12 3456 7890 1234"}""") };

        var result = await client.GetDebtorRefundIbanAsync("payment-1");

        Assert.Equal("/v3/payments/payment-1/debtor/refundIban", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Equal("BE12 3456 7890 1234", result.Iban);
    }

    [Fact]
    public async Task CreateStaticQrPaymentAsync_PostsToCorrectPath()
    {
        var (client, handler) = CreateClient();
        handler.ResponseFactory = () => new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent(SampleCreateResponseJson) };

        var result = await client.CreateStaticQrPaymentAsync(new CreateStaticQrPaymentRequest(Amount: 1000, PosId: "pos-1"));

        Assert.Equal("/v3/payments/pos", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("\"posId\":\"pos-1\"", handler.LastRequestBody);
        Assert.Equal("5f91483d-78a7-4914-bc6f=", result.PaymentId);
    }

    [Fact]
    public async Task GetAsync_NonSuccessResponse_ThrowsBancontactApiException()
    {
        var (client, handler) = CreateClient();
        handler.ResponseFactory = () => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("""{"code":"PAYMENT_NOT_FOUND","message":"not found","traceId":"t1","spanId":"s1"}"""),
        };

        var ex = await Assert.ThrowsAsync<BancontactApiException>(() => client.GetAsync("missing"));
        Assert.Equal("PAYMENT_NOT_FOUND", ex.Code);
    }
}
