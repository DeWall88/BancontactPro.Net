using System.Text.Json;
using BancontactPro.Models;

namespace BancontactPro.Net.Tests.Models;

public class PaymentModelsTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    [Fact]
    public void CreatePaymentResponse_DeserializesFromSpecShapedJson()
    {
        const string json = """
        {
          "paymentId": "5f91483d-78a7-4914-bc6f=",
          "status": "PENDING",
          "createdAt": "2026-01-02T03:04:05.000Z",
          "expiresAt": "2026-01-02T03:19:05.000Z",
          "amount": 1000,
          "currency": "EUR",
          "creditor": { "profileId": "p1", "merchantId": "m1" },
          "_links": {
            "self": { "href": "https://api/self" },
            "deeplink": { "href": "https://payconiq.com/pay/2/x" },
            "qrcode": { "href": "https://qr/x" }
          }
        }
        """;

        var response = JsonSerializer.Deserialize<CreatePaymentResponse>(json, Options);

        Assert.NotNull(response);
        Assert.Equal("5f91483d-78a7-4914-bc6f=", response!.PaymentId);
        Assert.Equal(MerchantPaymentStatus.PENDING, response.Status);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 19, 5, TimeSpan.Zero), response.ExpiresAt);
        Assert.Equal("p1", response.Creditor.ProfileId);
        Assert.Equal("https://qr/x", response.Links.Qrcode.Href);
        Assert.Null(response.Links.Cancel);
    }

    [Fact]
    public void Payment_DeserializesFromSpecShapedJson_IncludingOptionalDebtor()
    {
        const string json = """
        {
          "paymentId": "5f91483d-78a7-4914-bc6f=",
          "createdAt": "2026-01-02T03:04:05.000Z",
          "expireAt": "2026-01-02T03:19:05.000Z",
          "currency": "EUR",
          "status": "AUTHORIZED",
          "creditor": {},
          "debtor": { "iban": "*************12636" },
          "amount": 1000,
          "_links": {
            "self": { "href": "https://api/self" },
            "deeplink": { "href": "https://payconiq.com/pay/2/x" },
            "qrcode": { "href": "https://qr/x" }
          }
        }
        """;

        var payment = JsonSerializer.Deserialize<Payment>(json, Options);

        Assert.NotNull(payment);
        Assert.Equal(MerchantPaymentStatus.AUTHORIZED, payment!.Status);
        Assert.Equal("*************12636", payment.Debtor?.Iban);
        Assert.Null(payment.Debtor?.Name);
        Assert.Null(payment.SucceededAt);
    }

    [Fact]
    public void PaymentSearchResponse_DeserializesListOfPayments()
    {
        const string json = """
        {
          "size": 1,
          "totalPages": 1,
          "totalElements": 1,
          "number": 0,
          "details": [
            {
              "paymentId": "5f91483d-78a7-4914-bc6f=",
              "createdAt": "2026-01-02T03:04:05.000Z",
              "expireAt": "2026-01-02T03:19:05.000Z",
              "currency": "EUR",
              "status": "SUCCEEDED",
              "creditor": {},
              "amount": 500,
              "_links": {
                "self": { "href": "https://api/self" },
                "deeplink": { "href": "https://payconiq.com/pay/2/x" },
                "qrcode": { "href": "https://qr/x" }
              }
            }
          ]
        }
        """;

        var response = JsonSerializer.Deserialize<PaymentSearchResponse>(json, Options);

        Assert.NotNull(response);
        Assert.Single(response!.Details);
        Assert.Equal(MerchantPaymentStatus.SUCCEEDED, response.Details[0].Status);
    }

    [Fact]
    public void CreatePaymentRequest_SerializesOmittingUnsetOptionalFields()
    {
        var request = new CreatePaymentRequest(Amount: 1000);

        var json = JsonSerializer.Serialize(request, Options);

        Assert.Contains("\"amount\":1000", json);
    }

    [Fact]
    public void ErrorResponse_DeserializesFromSpecShapedJson()
    {
        const string json = """{"code":"PAYMENT_NOT_FOUND","message":"not found","traceId":"t1","spanId":"s1"}""";

        var error = JsonSerializer.Deserialize<ErrorResponse>(json, Options);

        Assert.Equal("PAYMENT_NOT_FOUND", error!.Code);
        Assert.Equal("t1", error.TraceId);
    }

    [Fact]
    public void DebtorRefundIban_DeserializesFromSpecShapedJson()
    {
        const string json = """{"iban":"BE12 3456 7890 1234"}""";

        var result = JsonSerializer.Deserialize<DebtorRefundIban>(json, Options);

        Assert.Equal("BE12 3456 7890 1234", result!.Iban);
    }
}
