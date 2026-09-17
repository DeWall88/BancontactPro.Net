using System.Net;
using BancontactPro.Http;

namespace BancontactPro.Net.Tests.Http;

public class HttpResponseMessageExtensionsTests
{
    [Fact]
    public async Task EnsureBancontactSuccessAsync_SuccessStatus_DoesNothing()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);

        await response.EnsureBancontactSuccessAsync();
    }

    [Fact]
    public async Task EnsureBancontactSuccessAsync_WellFormedError_ThrowsWithAllFields()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("""{"code":"PAYMENT_NOT_FOUND","message":"no payment found","traceId":"t-1","spanId":"s-1"}"""),
        };

        var ex = await Assert.ThrowsAsync<BancontactApiException>(() => response.EnsureBancontactSuccessAsync());

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        Assert.Equal("PAYMENT_NOT_FOUND", ex.Code);
        Assert.Equal("no payment found", ex.Message);
        Assert.Equal("t-1", ex.TraceId);
        Assert.Equal("s-1", ex.SpanId);
    }

    [Fact]
    public async Task EnsureBancontactSuccessAsync_RefundOrReconciliationShapedError_ThrowsWithNullTraceAndSpanId()
    {
        // Refund and Reconciliation specs' ErrorResponse only defines code/message -- no traceId/spanId at all.
        using var response = new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("""{"code":"REFUND_NOT_ALLOWED","message":"not allowed"}"""),
        };

        var ex = await Assert.ThrowsAsync<BancontactApiException>(() => response.EnsureBancontactSuccessAsync());

        Assert.Equal("REFUND_NOT_ALLOWED", ex.Code);
        Assert.Null(ex.TraceId);
        Assert.Null(ex.SpanId);
    }

    [Fact]
    public async Task EnsureBancontactSuccessAsync_NonJsonBody_FallsBackWithoutThrowingJsonException()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html>upstream error</html>"),
        };

        var ex = await Assert.ThrowsAsync<BancontactApiException>(() => response.EnsureBancontactSuccessAsync());

        Assert.Equal(HttpStatusCode.BadGateway, ex.StatusCode);
        Assert.Contains("502", ex.Message);
    }

    [Fact]
    public async Task EnsureBancontactSuccessAsync_EmptyBody_FallsBackGracefully()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent(""),
        };

        var ex = await Assert.ThrowsAsync<BancontactApiException>(() => response.EnsureBancontactSuccessAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
    }
}
