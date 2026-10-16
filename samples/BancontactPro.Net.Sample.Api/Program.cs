using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using BancontactPro.Helpers;
using BancontactPro.Http;
using BancontactPro.Models;
using BancontactPro.QrCodes;
using BancontactPro.Refunds;
using BancontactPro.Webhooks;

namespace BancontactPro.Sample.Api;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        // Static settings (environment variables, user secrets, appsettings), validated when the app starts. A host whose
        // credentials change at runtime, or that must start with none, registers an IBancontactCredentialsProvider with
        // AddBancontactPro<TProvider>() instead: see docs/getting-started.md, "Register the integration".
        builder.Services.AddBancontactProIntegration(builder.Configuration);
        builder.Services.AddSingleton<CallbackLog>();
        // Enums as names ("SUCCEEDED", "EUR") in requests and responses, as Bancontact writes them.
        builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        var app = builder.Build();
        app.MapSampleEndpoints();
        app.Run();
    }
}

/// <summary>
/// A pass-through over the library: every route calls exactly one library method, which calls exactly one
/// Bancontact endpoint. Routes are Bancontact's own paths with the product in front, e.g.
/// <c>POST /OnDisplay/v3/payments</c> → <c>clients.PaymentsAsync(OnDisplay)</c> then <c>CreateAsync</c> →
/// <c>POST https://merchant.api…/v3/payments</c> with On Display's API key. Bodies are Bancontact's own request
/// and response shapes, and Bancontact's errors come back with their status, code and trace ID.
/// Which product uses which call is Bancontact's documentation's business (see docs/getting-started.md), not
/// this sample's: it forwards whatever you send.
/// </summary>
public static class SampleEndpoints
{
    /// <summary>The path Bancontact calls. It must match the signed <c>path</c> claim, so a proxy in front must not rewrite it.</summary>
    public const string CallbackPath = "/bancontact/callback";

    /// <summary>The header carrying the shared key every route except <c>/health</c> and the callback requires.</summary>
    public const string ApiKeyHeader = "X-Sample-Key";

    public static void MapSampleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        app.MapPost(CallbackPath, async (HttpRequest request, BancontactCallbackVerifier verifier, CallbackLog log, ILogger<Program> logger, CancellationToken ct) =>
        {
            // Verify the exact bytes received: re-serialising a model first would change them and break the signature.
            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer, ct);

            VerifiedCallback verified;
            try
            {
                verified = await verifier.VerifyAndParseAsync(request.Headers["Signature"].ToString(), buffer.ToArray(), request.Path, ct);
            }
            catch (InvalidCallbackException ex)
            {
                logger.LogWarning(ex, "Rejected a callback that failed verification");
                return Results.Unauthorized();
            }

            log.Add(new ReceivedCallback(DateTimeOffset.UtcNow, verified.RequestId, verified.Payload));
            logger.LogInformation("Callback {RequestId} for {PaymentId}: {Status}", verified.RequestId, verified.Payload.PaymentId, verified.Payload.Status);

            // 2xx once the signature is valid, so Bancontact does not retry.
            return Results.Ok();
        });

        // Everything below calls Bancontact on your behalf, and this app is reachable from the internet (Bancontact has
        // to reach the callback), so it sits behind a shared key. No key configured means every request is refused.
        var api = app.MapGroup("")
            .AddEndpointFilter(RequireSampleKey)
            .AddEndpointFilter(TranslateErrors);

        api.MapGet("/callbacks", (string? paymentId, CallbackLog log) => Results.Ok(log.Recent(paymentId)));

        var perProduct = api.MapGroup("/{product}");
        MapPayments(perProduct);
        MapRefunds(perProduct);
        MapReconciliation(perProduct);
        MapQrCodes(perProduct);
    }

    private static void MapPayments(RouteGroupBuilder routes)
    {
        routes.MapPost("/v3/payments", async (string product, CreatePaymentRequest body, IBancontactClientFactory clients, IConfiguration config, HttpContext http, CancellationToken ct) =>
        {
            var created = await (await clients.PaymentsAsync(ParseProduct(product), ct)).CreateAsync(body with { CallbackUrl = body.CallbackUrl ?? DefaultCallbackUrl(config, http) }, ct);
            return Results.Json(created, statusCode: StatusCodes.Status201Created);
        });

        routes.MapPost("/v3/payments/pos", async (string product, CreateStaticQrPaymentRequest body, IBancontactClientFactory clients, IConfiguration config, HttpContext http, CancellationToken ct) =>
        {
            var created = await (await clients.PaymentsAsync(ParseProduct(product), ct)).CreateStaticQrPaymentAsync(body with { CallbackUrl = body.CallbackUrl ?? DefaultCallbackUrl(config, http) }, ct);
            return Results.Json(created, statusCode: StatusCodes.Status201Created);
        });

        routes.MapGet("/v3/payments/{paymentId}", async (string product, string paymentId, IBancontactClientFactory clients, CancellationToken ct) =>
            Results.Ok(await (await clients.PaymentsAsync(ParseProduct(product), ct)).GetAsync(paymentId, ct)));

        routes.MapDelete("/v3/payments/{paymentId}", async (string product, string paymentId, IBancontactClientFactory clients, CancellationToken ct) =>
        {
            await (await clients.PaymentsAsync(ParseProduct(product), ct)).CancelAsync(paymentId, ct);
            return Results.NoContent();
        });

        routes.MapPost("/v3/payments/search", async (string product, PaymentSearchQuery body, int? page, int? size, IBancontactClientFactory clients, CancellationToken ct) =>
            Results.Ok(await (await clients.PaymentsAsync(ParseProduct(product), ct)).SearchAsync(body, page, size, ct)));

        routes.MapPost("/v3/payments/{paymentId}/acknowledge", async (string product, string paymentId, PaymentAcknowledgeRequest body, IBancontactClientFactory clients, CancellationToken ct) =>
        {
            await (await clients.PaymentsAsync(ParseProduct(product), ct)).AcknowledgeAsync(paymentId, body, ct);
            return Results.NoContent();
        });

        routes.MapGet("/v3/payments/{paymentId}/debtor/refundIban", async (string product, string paymentId, IBancontactClientFactory clients, CancellationToken ct) =>
            Results.Ok(await (await clients.PaymentsAsync(ParseProduct(product), ct)).GetDebtorRefundIbanAsync(paymentId, ct)));
    }

    private static void MapRefunds(RouteGroupBuilder routes)
    {
        routes.MapPost("/v3/payments/{paymentId}/refunds", async (string product, string paymentId, CreateRefundRequest body, HttpRequest request, IBancontactClientFactory clients, CancellationToken ct) =>
        {
            var idempotencyKey = request.Headers["Idempotency-Key"].ToString();
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                throw new SampleRequestException(StatusCodes.Status400BadRequest, "Idempotency-Key header missing",
                    "Creating a refund needs an Idempotency-Key header: generate one per refund attempt and reuse it when retrying.");
            }

            var created = await (await clients.RefundsAsync(ParseProduct(product), ct)).CreateAsync(paymentId, body, idempotencyKey, ct);
            return Results.Json(created, statusCode: StatusCodes.Status201Created);
        });

        routes.MapGet("/v3/payments/{paymentId}/refunds/{refundId}", async (string product, string paymentId, string refundId, IBancontactClientFactory clients, CancellationToken ct) =>
            Results.Ok(await (await clients.RefundsAsync(ParseProduct(product), ct)).GetAsync(paymentId, refundId, ct)));
    }

    private static void MapReconciliation(RouteGroupBuilder routes)
    {
        routes.MapGet("/v3/reconciliation/payouts", async (string product, DateOnly? date, int? page, int? size, IBancontactClientFactory clients, CancellationToken ct) =>
            Results.Ok(await (await clients.ReconciliationAsync(ParseProduct(product), ct)).GetPayoutsAsync(date, page, size, ct)));

        routes.MapGet("/v3/reconciliation/payments", async (string product, string? payoutId, DateOnly? startDate, DateOnly? endDate, int? page, int? size, IBancontactClientFactory clients, CancellationToken ct) =>
            Results.Ok(await (await clients.ReconciliationAsync(ParseProduct(product), ct)).GetPaymentsAsync(payoutId, startDate, endDate, page, size, ct)));

        routes.MapGet("/v3/reconciliation/refunds", async (string product, string? payoutId, DateOnly? startDate, DateOnly? endDate, int? page, int? size, IBancontactClientFactory clients, CancellationToken ct) =>
            Results.Ok(await (await clients.ReconciliationAsync(ParseProduct(product), ct)).GetRefundsAsync(payoutId, startDate, endDate, page, size, ct)));
    }

    // Not API calls: PaymentLinkBuilder builds the QR payload URLs Bancontact's guides describe, with the product's PPID,
    // on the hosts of the configured environment.
    private static void MapQrCodes(RouteGroupBuilder routes)
    {
        routes.MapGet("/qr/payment-url", async (string product, long? amount, string? description, string? reference, string? format, string? size, IBancontactCredentialsProvider credentialsProvider, CancellationToken ct) =>
        {
            var credentials = await credentialsProvider.GetCredentialsAsync(ct);
            var environment = credentials.Environment;
            var payloadUrl = PaymentLinkBuilder.BuildFixedAmountPaymentUrl(environment, Ppid(credentials, ParseProduct(product)), amount, description, reference);
            return Results.Ok(new QrCodeLinks(payloadUrl, PaymentLinkBuilder.BuildQrCodeImageUrl(environment, payloadUrl, ParseFormat(format), ParseSize(size))));
        });

        routes.MapGet("/qr/pos/{posId}", async (string product, string posId, string? format, string? size, IBancontactCredentialsProvider credentialsProvider, CancellationToken ct) =>
        {
            var credentials = await credentialsProvider.GetCredentialsAsync(ct);
            var environment = credentials.Environment;
            var payloadUrl = PaymentLinkBuilder.BuildStaticQrLocationUrl(environment, Ppid(credentials, ParseProduct(product)), posId);
            return Results.Ok(new QrCodeLinks(payloadUrl, PaymentLinkBuilder.BuildQrCodeImageUrl(environment, payloadUrl, ParseFormat(format), ParseSize(size))));
        });
    }

    /// <summary>The payload a QR code carries, and the URL of Bancontact's generator rendering it as an image.</summary>
    public sealed record QrCodeLinks(string PayloadUrl, string QrCodeImageUrl);

    /// <summary>
    /// When a create call names no callbackUrl, point it at this app so its callbacks show up in <c>GET /callbacks</c>.
    /// PublicBaseUrl is the address Bancontact reaches the app at; behind a reverse proxy or tunnel the request's own
    /// scheme and host are the internal ones, so they are only the fallback for local runs.
    /// </summary>
    private static string DefaultCallbackUrl(IConfiguration config, HttpContext http) =>
        (config["PublicBaseUrl"]?.TrimEnd('/') ?? $"{http.Request.Scheme}://{http.Request.Host}") + CallbackPath;

    private static string Ppid(BancontactCredentials credentials, BancontactProduct product) =>
        credentials.GetCredentials(product)?.MerchantProfileId ?? throw new BancontactProductNotConfiguredException(product);

    private static BancontactProduct ParseProduct(string value) =>
        Enum.TryParse<BancontactProduct>(value, ignoreCase: true, out var product) && Enum.IsDefined(product)
            ? product
            : throw new SampleRequestException(StatusCodes.Status404NotFound, $"Unknown product '{value}'",
                $"The first path segment is the product: {string.Join(", ", Enum.GetNames<BancontactProduct>())}.");

    private static QrCodeImageFormat ParseFormat(string? value) =>
        value is null ? QrCodeImageFormat.PNG
        : Enum.TryParse<QrCodeImageFormat>(value, ignoreCase: true, out var format) && Enum.IsDefined(format) ? format
        : throw new SampleRequestException(StatusCodes.Status400BadRequest, $"Unknown format '{value}'", "format is PNG or SVG.");

    private static QrCodeImageSize? ParseSize(string? value) =>
        value is null ? null
        : Enum.TryParse<QrCodeImageSize>(value, ignoreCase: true, out var size) && Enum.IsDefined(size) ? size
        : throw new SampleRequestException(StatusCodes.Status400BadRequest, $"Unknown size '{value}'", "size is S, M, L or XL (PNG only).");

    private static async ValueTask<object?> RequireSampleKey(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var expected = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>()["SampleApiKey"];
        var supplied = context.HttpContext.Request.Headers[ApiKeyHeader].ToString();
        var valid = !string.IsNullOrEmpty(expected)
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(supplied));
        return valid ? await next(context) : Results.Unauthorized();
    }

    /// <summary>Turns the library's exceptions into problem responses, so a failed call says why instead of returning an empty 500.</summary>
    private static async ValueTask<object?> TranslateErrors(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (SampleRequestException ex)
        {
            return Results.Problem(ex.Message, statusCode: ex.StatusCode, title: ex.Title);
        }
        catch (BancontactProductNotConfiguredException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest, title: $"Product {ex.Product} is not configured");
        }
        catch (BancontactSigningKeyNotConfiguredException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest, title: "No signing key configured");
        }
        catch (RefundIdempotencyConflictException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict, title: "Idempotency-Key reused with different refund parameters");
        }
        catch (BancontactApiException ex)
        {
            // Bancontact's own status, code and trace IDs, as it sent them: the trace and span IDs are what its support asks for.
            var extensions = new Dictionary<string, object?> { ["code"] = ex.Code, ["traceId"] = ex.TraceId, ["spanId"] = ex.SpanId };
            if (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                // The usual cause: the product's API key and PPID look alike and are easily swapped.
                extensions["hint"] = "Bancontact did not accept this product's API key. Check BancontactPro:Products:<Product>:ApiKey " +
                                     "is the API key (not the PPID) from that product's entry, for this environment.";
            }

            return Results.Problem(ex.Message, statusCode: (int)ex.StatusCode, title: $"Bancontact returned {(int)ex.StatusCode} {ex.Code}".TrimEnd(), extensions: extensions);
        }
        catch (HttpRequestException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway, title: "Could not reach Bancontact");
        }
    }
}

/// <summary>A request the sample itself refuses (unknown product, missing header, ...), with the status to answer.</summary>
public sealed class SampleRequestException(int statusCode, string title, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public string Title { get; } = title;
}
