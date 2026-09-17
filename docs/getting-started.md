# Getting started

A step-by-step guide to integrating BancontactPro.Net into an application, from onboarding
through your first payment and webhook. For *why* things work the way they do (the signing
scheme, the JWKS caching, the client internals), see [architecture.md](architecture.md).

This library wraps three of Bancontact's own APIs, whose official documentation and raw
OpenAPI specs are the authoritative source for anything not covered here:

- [Bancontact Pro developer portal](https://docs.bancontactpro.com/) — overview, guides, and
  the interactive API reference.
- [Payment API spec](https://docs.bancontactpro.com/_bundle/apis/merchant-payment.openapi.json)
  (raw OpenAPI JSON, v3.6.5 as of this writing) — backs `IPaymentClient`.
- [Refund API spec](https://docs.bancontactpro.com/_bundle/apis/refund-public.openapi.json)
  (v3.0.3) — backs `IRefundClient`.
- [Reconciliation API spec](https://docs.bancontactpro.com/_bundle/apis/merchant-reconciliation.openapi.json)
  (v3.0.1) — backs `IReconciliationClient`.

## 1. Onboarding (do this first — it has a lead time)

Bancontact Pro isn't self-serve. Before you can call anything:

1. **Pre-production**: email [devsupport@bancontact.com](mailto:devsupport@bancontact.com) with
   your company name, Merchant ID, contact details, and which integration type(s) you need (On a
   Display, On a Receipt, Static QR, Top Up). Expect up to **two weeks** turnaround. They'll issue
   a Product Profile ID (PPID) and API key per integration type.
2. **Production**: apply via the merchant portal once pre-production is verified working (the
   application portal is separate from the [developer docs](https://docs.bancontactpro.com/) —
   devsupport will point you to it).

You'll also need to generate your own signing key pair (step 2) and share the public half with
Bancontact during onboarding — they need it before your merchant profile can accept signed
requests from you.

## 2. Generate a signing key pair

Bancontact verifies your outgoing requests using ES256 (ECDSA on the P-256 curve). Generate a
key pair with OpenSSL:

```bash
openssl ecparam -name prime256v1 -genkey -noout -out bancontact-private-key.pem
```

This produces a PEM file — its contents go into `PrivateKeyPem` in configuration (see step 4).
**Treat it like any other private key**: user secrets or a secret manager in development, a
proper secret store (Key Vault, AWS Secrets Manager, etc.) in production. Never commit it.

Export the public half as a JWK to hand to Bancontact:

```csharp
using var key = System.Security.Cryptography.ECDsa.Create();
key.ImportFromPem(File.ReadAllText("bancontact-private-key.pem"));

var kid = "your-chosen-key-id"; // any string; you choose this, Bancontact just needs to know it
Console.WriteLine(BancontactPro.Signing.JwkExporter.ToPublicJwkJson(key, kid));
```

Bancontact needs this JWK reachable at a URL you control (your own `/.well-known/jwks.json` or
similar) — **this library doesn't host that endpoint for you**; it's a small amount of extra
plumbing in your own application (an ASP.NET Core minimal API endpoint returning the JSON is
enough). Whatever URL you choose, register it with Bancontact during onboarding along with the
`kid` you picked.

## 3. Install

```bash
dotnet add package BancontactPro.Net
```

## 4. Configure

```json
{
  "BancontactPro": {
    "ApiKey": "the API key devsupport issued for this integration",
    "MerchantProfileId": "the PPID devsupport issued for this integration",
    "Kid": "your-chosen-key-id",
    "PrivateKeyPem": "-----BEGIN PRIVATE KEY-----\n...\n-----END PRIVATE KEY-----",
    "Environment": "Preprod"
  }
}
```

Keep `ApiKey` and `PrivateKeyPem` out of source control — use `dotnet user-secrets` locally:

```bash
dotnet user-secrets set "BancontactPro:ApiKey" "..."
dotnet user-secrets set "BancontactPro:PrivateKeyPem" "$(cat bancontact-private-key.pem)"
```

Switch `Environment` to `Production` once you're ready to go live — this changes which host
every client and the JWKS fetcher talk to (`merchant.api.preprod.bancontact.net` /
`jwks.preprod.bancontact.net` vs. their non-`preprod` production equivalents).

## 5. Register the integration

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddBancontactProIntegration(builder.Configuration);
```

This single call registers everything: the signing handler, the JWKS client, the callback
verifier, and `IPaymentClient`/`IRefundClient`/`IReconciliationClient` as typed, pre-signed
`HttpClient`s. Configuration is validated on first access — a missing `ApiKey`, `Kid`, or an
unparseable `PrivateKeyPem` throws `OptionsValidationException` with a specific message rather
than failing confusingly later on the first real API call.

## 6. Create a payment

Corresponds to `POST /v3/payments` in the
[Payment API spec](https://docs.bancontactpro.com/_bundle/apis/merchant-payment.openapi.json).

```csharp
public class CheckoutService(IPaymentClient paymentClient)
{
    public async Task<string> StartCheckoutAsync(long amountInCents, string orderReference)
    {
        var request = new CreatePaymentRequest(
            Amount: amountInCents,
            Reference: orderReference,
            Description: "Order #" + orderReference);
            // CallbackUrl/ReturnUrl/IdentifyCallbackUrl are optional -- omitted here, they fall
            // back to whatever URLs are configured on your merchant profile in the portal.

        var payment = await paymentClient.CreateAsync(request);

        // Either redirect the customer to the hosted checkout page...
        return payment.Links.Checkout?.Href
            // ...or render payment.Links.Qrcode.Href yourself if you want a fully custom UI.
            ?? payment.Links.Qrcode.Href;
    }
}
```

The response's `PaymentId` is valid for 20 minutes. `Links.Self` is the polling URL (see step
8) — it is **not** the QR code; `Links.Qrcode` is.

## 7. Handle the webhook callback

Bancontact calls back asynchronously once the customer confirms (or the payment fails/expires).
**Callback and redirect ordering isn't guaranteed** — always treat the callback as the primary
signal and use polling (step 8) as a fallback, not the other way around. The callback body shape
is the `merchant-callback` schema in the
[Payment API spec](https://docs.bancontactpro.com/_bundle/apis/merchant-payment.openapi.json).

```csharp
app.MapPost("/bancontact/callback", async (HttpRequest request, BancontactCallbackVerifier verifier, IMyOrderStore orders) =>
{
    using var reader = new StreamReader(request.Body);
    var body = Encoding.UTF8.GetBytes(await reader.ReadToEndAsync());
    var signatureHeader = request.Headers["Signature"].ToString();

    VerifiedCallback result;
    try
    {
        result = await verifier.VerifyAndParseAsync(signatureHeader, body, request.Path);
    }
    catch (InvalidCallbackException)
    {
        // Signature didn't verify, or the body didn't match the expected schema. Reject it --
        // don't act on an unverifiable callback.
        return Results.BadRequest();
    }

    // Bancontact retries non-200 responses for up to 24h, so duplicate callbacks for the same
    // payment/status are expected. Dedupe using RequestId (the JOSE header's `jti` claim), e.g.
    // an idempotency table keyed on it, before applying result.Payload.Status.
    if (await orders.HasProcessedAsync(result.RequestId))
    {
        return Results.Ok();
    }

    await orders.ApplyStatusAsync(result.Payload.PaymentId, result.Payload.Status);
    await orders.MarkProcessedAsync(result.RequestId);

    return Results.Ok();
});
```

Make sure this endpoint reads the **raw** request body — the signature covers the exact bytes
Bancontact sent, so any middleware that re-serializes or reformats the body before this handler
sees it (a JSON model-binder, for instance) will break verification. Reading `request.Body`
directly, as above, avoids that.

## 8. Poll as a fallback

```csharp
var payment = await paymentClient.GetAsync(paymentId);
if (payment.Status is MerchantPaymentStatus.SUCCEEDED or MerchantPaymentStatus.FAILED
    or MerchantPaymentStatus.CANCELLED or MerchantPaymentStatus.EXPIRED)
{
    // terminal state -- stop polling
}
```

## 9. Cancel a payment

Only possible while `PENDING`/`IDENTIFIED`:

```csharp
await paymentClient.CancelAsync(paymentId); // throws BancontactApiException (PAYMENT_NOT_PENDING) otherwise
```

## 10. Issue a refund

Corresponds to `POST /v3/payments/{payment-id}/refunds` in the
[Refund API spec](https://docs.bancontactpro.com/_bundle/apis/refund-public.openapi.json).

```csharp
var refund = await refundClient.CreateAsync(
    paymentId,
    new CreateRefundRequest(Amount: 500, Currency: "EUR", Description: "Partial refund"),
    idempotencyKey: Guid.NewGuid().ToString()); // generate once per distinct refund attempt, reuse on retry

// refund.Status is always PENDING right after creation -- poll for the real outcome:
var current = await refundClient.GetAsync(paymentId, refund.RefundId);
```

**Reusing an idempotency key with different parameters** (e.g. retrying with a different amount)
throws `RefundIdempotencyConflictException` rather than a generic `BancontactApiException` —
that's specifically a caller bug (the key no longer identifies the same logical refund attempt),
not a transient failure worth blindly retrying.

If you need to transfer a refund manually instead (rare — most refunds go through the Refund
API above), `GetDebtorRefundIbanAsync` on `IPaymentClient` (`GET
/v3/payments/{id}/debtor/refundIban` in the
[Payment API spec](https://docs.bancontactpro.com/_bundle/apis/merchant-payment.openapi.json) —
yes, it lives there rather than in the Refund spec) returns the customer's unmasked IBAN, but
only once the payment is `SUCCEEDED`.

## 11. Static QR (point-of-sale) payments

For in-person/POS integrations rather than online checkout. Corresponds to `POST
/v3/payments/pos` in the
[Payment API spec](https://docs.bancontactpro.com/_bundle/apis/merchant-payment.openapi.json):

```csharp
var payment = await paymentClient.CreateStaticQrPaymentAsync(
    new CreateStaticQrPaymentRequest(Amount: 1000, PosId: "till-1", ShopName: "My Shop"));
```

Creating a new static QR payment for the same `PosId`/profile combination invalidates any
existing active one for that POS — this is by design (it's how a physical, reusable QR code at
a till gets "refreshed" for the next customer), not something to guard against client-side.

## 12. Reconciliation (accounting, not the checkout flow)

Backed by the
[Reconciliation API spec](https://docs.bancontactpro.com/_bundle/apis/merchant-reconciliation.openapi.json).
Data is only available **D+1, starting 09:00 CET** — don't build same-day reconciliation UX.

```csharp
var payouts = await reconciliationClient.GetPayoutsAsync(date: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)));

foreach (var payout in payouts.Payouts)
{
    var payments = await reconciliationClient.GetPaymentsAsync(payoutId: payout.PayoutId);
    var refunds = await reconciliationClient.GetRefundsAsync(payoutId: payout.PayoutId);
    // reconcile payout.PayoutAmount against the sum of payments/refunds
}
```

Filtering by date range instead of payout id is also supported (`startDate`/`endDate`, max 30
days apart, both required together) — useful if you reconcile on a schedule rather than
per-payout.

## Error handling summary

| Exception | Thrown by | When |
|---|---|---|
| `BancontactApiException` | any client method | Bancontact returned a non-success HTTP status. Carries `StatusCode`, `Code`, `Message`, and (Payment API only) `TraceId`/`SpanId` for support requests. |
| `RefundIdempotencyConflictException` | `IRefundClient.CreateAsync` | The `Idempotency-Key` was reused with different refund parameters — a caller bug, not transient. |
| `InvalidCallbackException` | `BancontactCallbackVerifier.VerifyAndParseAsync` | The callback's signature didn't verify, or its body didn't match the expected schema. |
| `OptionsValidationException` | first access of `BancontactProOptions` | Required configuration is missing, or `PrivateKeyPem` isn't a parseable PEM-encoded EC key. |

See [architecture.md](architecture.md#error-handling) for why `TraceId`/`SpanId` aren't always
populated.
