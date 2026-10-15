using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using BancontactPro.Helpers;
using BancontactPro.Models;
using BancontactPro.Webhooks;

namespace BancontactPro.Sample.Api;

/// <summary>A payment to create. <c>PosId</c> makes it a Static QR payment for that point of sale.</summary>
public record CreatePaymentBody(BancontactProduct Product, long Amount, string? Reference, string? Description, string? PosId);

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddBancontactProIntegration(builder.Configuration);
        builder.Services.AddSingleton<IPaymentStore, InMemoryPaymentStore>();
        // Products and statuses as names ("StaticQr", "SUCCEEDED") rather than numbers, in requests and responses.
        builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        var app = builder.Build();
        app.MapSampleEndpoints();
        app.Run();
    }
}

public static class SampleEndpoints
{
    /// <summary>The path Bancontact calls. It must match the signed <c>path</c> claim, so a proxy in front must not rewrite it.</summary>
    public const string CallbackPath = "/bancontact/callback";

    /// <summary>The header carrying the shared key the payment endpoints require.</summary>
    public const string ApiKeyHeader = "X-Sample-Key";

    public static void MapSampleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        // The payment endpoints sit behind a shared key: this app is reachable from the internet (Bancontact has to reach the callback), and
        // creating or cancelling payments must not be open to everyone. No key configured means every request is refused.
        var payments = app.MapGroup("/payments")
            .AddEndpointFilter(async (context, next) =>
            {
                var expected = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>()["SampleApiKey"];
                var supplied = context.HttpContext.Request.Headers[ApiKeyHeader].ToString();
                var valid = !string.IsNullOrEmpty(expected)
                    && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(supplied));
                return valid ? await next(context) : Results.Unauthorized();
            })
            .AddEndpointFilter(async (context, next) =>
            {
                // A product without its API key and PPID can't be called: say which settings are missing instead of a 500.
                try
                {
                    return await next(context);
                }
                catch (BancontactProductNotConfiguredException ex)
                {
                    return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest, title: $"Product {ex.Product} is not configured");
                }
            });

        payments.MapPost("/", async (CreatePaymentBody body, IBancontactClientFactory clients, IPaymentStore store, IConfiguration config, HttpContext http, CancellationToken ct) =>
        {
            var client = clients.Payments(body.Product);

            // PublicBaseUrl is the public address Bancontact reaches the app at. Behind a reverse proxy or tunnel the request's own
            // scheme and host are the internal ones, so they are only the fallback for local runs.
            var publicBaseUrl = config["PublicBaseUrl"]?.TrimEnd('/') ?? $"{http.Request.Scheme}://{http.Request.Host}";
            var callbackUrl = $"{publicBaseUrl}{CallbackPath}";
            var created = body.PosId is { Length: > 0 } posId
                ? await client.CreateStaticQrPaymentAsync(new CreateStaticQrPaymentRequest(body.Amount, posId, body.Reference, Description: body.Description, CallbackUrl: callbackUrl), ct)
                : await client.CreateAsync(new CreatePaymentRequest(body.Amount, body.Reference, Description: body.Description, CallbackUrl: callbackUrl), ct);

            var record = new PaymentRecord(created.PaymentId, body.Product, created.Status, created.Amount, created.Reference, created.Links.Qrcode.Href, created.CreatedAt);
            store.Add(record);
            return Results.Created($"/payments/{created.PaymentId}", record);
        });

        // ?refresh=true asks Bancontact instead of trusting the stored status: the fallback when a callback is late or lost.
        // The payment's own product decides which credentials are used.
        payments.MapGet("/{paymentId}", async (string paymentId, bool? refresh, IBancontactClientFactory clients, IPaymentStore store, CancellationToken ct) =>
        {
            if (store.Get(paymentId) is not { } record)
            {
                return Results.NotFound();
            }

            if (refresh == true)
            {
                var payment = await clients.Payments(record.Product).GetAsync(paymentId, ct);
                store.TryApplyStatus(paymentId, payment.Status, DateTimeOffset.UtcNow);
            }

            return Results.Ok(store.Get(paymentId));
        });

        payments.MapDelete("/{paymentId}", async (string paymentId, IBancontactClientFactory clients, IPaymentStore store, CancellationToken ct) =>
        {
            if (store.Get(paymentId) is not { } record)
            {
                return Results.NotFound();
            }

            await clients.Payments(record.Product).CancelAsync(paymentId, ct);
            store.TryApplyStatus(paymentId, MerchantPaymentStatus.CANCELLED, DateTimeOffset.UtcNow);
            return Results.NoContent();
        });

        app.MapPost(CallbackPath, async (HttpRequest request, BancontactCallbackVerifier verifier, IPaymentStore store, ILogger<Program> logger, CancellationToken ct) =>
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

            var callback = verified.Payload;
            var applied = store.TryApplyStatus(callback.PaymentId, callback.Status, callback.SucceededAt ?? DateTimeOffset.UtcNow);
            logger.LogInformation("Callback {RequestId} for {PaymentId}: {Status} (applied: {Applied})", verified.RequestId, callback.PaymentId, callback.Status, applied);

            // Always 2xx once the signature is valid, including duplicates and unknown payments, so Bancontact does not retry.
            return Results.Ok();
        });
    }
}
