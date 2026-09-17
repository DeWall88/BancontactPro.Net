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
- Prose guides, all under the same product family:
  [Getting Started](https://docs.bancontactpro.com/guides/general/gettingstarted052025v4),
  [Callback Guide](https://docs.bancontactpro.com/guides/general/callback052025),
  [Errors, statuses and responses](https://docs.bancontactpro.com/guides/general/errorsandstatuses052025),
  [On a Display](https://docs.bancontactpro.com/guides/instore/ondisplay052025v4),
  [On a Receipt](https://docs.bancontactpro.com/guides/instore/receipt052025v4),
  [Static QR](https://docs.bancontactpro.com/guides/instore/staticqr052025v4),
  [Top Up](https://docs.bancontactpro.com/guides/online/topup052025v4),
  [Refunds](https://docs.bancontactpro.com/guides/general/refunds052025),
  [Reconciliation](https://docs.bancontactpro.com/guides/general/reconciliation052025),
  [Payout & remittance](https://docs.bancontactpro.com/guides/general/payoutremittance052025).

**Not covered here**: Bancontact also documents an
[Online Sales](https://docs.bancontactpro.com/guides/online/onlinesales) product (hosted
checkout / redirect flow, the kind of thing a typical webshop reaches for) — as of this writing
that page states the product is "no longer offered directly by Bancontact Payconiq Company."
This library targets the four in-store QR products below instead. The underlying Payment API
calls are the same either way; what differs is the payment expiry window (see step 6) and which
`_links` field you're expected to use.

## 1. Onboarding (do this first — it has a lead time)

Bancontact Pro isn't self-serve. Before you can call anything:

1. **Pre-production**: email [devsupport@bancontact.com](mailto:devsupport@bancontact.com) with
   your company name, Merchant ID, contact details, and which integration type(s) you need:
   [On a Display](https://docs.bancontactpro.com/guides/instore/ondisplay052025v4),
   [On a Receipt](https://docs.bancontactpro.com/guides/instore/receipt052025v4),
   [Static QR](https://docs.bancontactpro.com/guides/instore/staticqr052025v4), or
   [Top Up](https://docs.bancontactpro.com/guides/online/topup052025v4). Expect up to **two
   weeks** turnaround. They'll issue a Product Profile ID (PPID) and API key per integration type.
2. **Production**: apply via the merchant portal once pre-production is verified working (the
   application portal is separate from the [developer docs](https://docs.bancontactpro.com/) —
   devsupport will point you to it).

Two more things to line up in parallel: generate your own signing key pair (step 2) and share
the public half with Bancontact during onboarding — they need it before your merchant profile
can accept signed requests from you — and install the Bancontact Pay test app (see
[Testing with the Bancontact Pay app](#testing-with-the-bancontact-pay-app) near the end of this
guide) so you can actually confirm a payment end-to-end once you're wired up.

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
[Payment API spec](https://docs.bancontactpro.com/_bundle/apis/merchant-payment.openapi.json) —
the same operation regardless of which in-store product (On a Display, On a Receipt, Static QR)
your merchant profile is configured for; what differs per product is the payment's validity
window and which `_links` field you're expected to render.

```csharp
public class PaymentService(IPaymentClient paymentClient)
{
    public async Task<string> CreateAsync(long amountInCents, string orderReference)
    {
        var request = new CreatePaymentRequest(
            Amount: amountInCents,
            Reference: orderReference,
            Description: "Order #" + orderReference);
            // CallbackUrl/IdentifyCallbackUrl are optional -- omitted here, they fall back to
            // whatever URLs are configured on your merchant profile in the portal.

        var payment = await paymentClient.CreateAsync(request);

        // Render this as a QR code on your display/receipt/POS -- see the guide for your
        // specific product for exact rendering parameters (size, format).
        return payment.Links.Qrcode.Href;
    }
}
```

**The payment's validity window isn't a fixed constant — it's set by your merchant profile's
configured product.** The
[On a Display guide](https://docs.bancontactpro.com/guides/instore/ondisplay052025v4) states
**2 minutes (120 seconds)**; the (discontinued) Online Sales product historically used 20
minutes. Don't hardcode either figure into your own UX countdown — treat it as configuration, or
just rely on the payment reaching a terminal status (see step 8) rather than timing it yourself.
`Links.Self` is the polling URL — it is **not** the QR code; `Links.Qrcode` is. `Links.Checkout`
is also present on the response, but it's a hosted-checkout-page URL primarily meaningful for
the discontinued Online Sales product — you likely don't need it for an in-store QR product.

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

## Testing with the Bancontact Pay app

To actually confirm a pre-production payment end-to-end, you need the Bancontact Pay app
(details per the
[Getting Started guide](https://docs.bancontactpro.com/guides/general/gettingstarted052025v4)):

- **Pre-production build**: download via [this form](https://tally.so/r/nP8P1Q) (not the App
  Store/Play Store build — install only one build at a time; having both installed causes
  redirection issues).
- **Production build**: the regular App Store (iOS) / Google Play Store (Android) listing.

Onboarding within the app itself uses a fixed placeholder code — no real OTP is sent:

1. Choose "I don't have itsme", enter your email, then enter code `123456` when prompted (no
   email is actually sent).
2. Enter your name, then your phone number (must be an EU number), then code `123456` again
   (again, no SMS is sent).
3. Set a PIN, optionally enable biometrics, then add a test card from the table below.

Test cards (all issued by KBC, expiring Nov-29 as of this writing — confirm current values in
the [Getting Started guide](https://docs.bancontactpro.com/guides/general/gettingstarted052025v4)
if this has aged):

| Card Number | Expected Result |
|---|---|
| `5127 8829 9999 9715` | Always authorized |
| `5127 8829 9999 9723` | Insufficient funds |
| `5127 8829 9999 9731` | Card refused by issuer |

## Error handling summary

| Exception | Thrown by | When |
|---|---|---|
| `BancontactApiException` | any client method | Bancontact returned a non-success HTTP status. Carries `StatusCode`, `Code`, `Message`, and (Payment API only) `TraceId`/`SpanId` for support requests. |
| `RefundIdempotencyConflictException` | `IRefundClient.CreateAsync` | The `Idempotency-Key` was reused with different refund parameters — a caller bug, not transient. |
| `InvalidCallbackException` | `BancontactCallbackVerifier.VerifyAndParseAsync` | The callback's signature didn't verify, or its body didn't match the expected schema. |
| `OptionsValidationException` | first access of `BancontactProOptions` | Required configuration is missing, or `PrivateKeyPem` isn't a parseable PEM-encoded EC key. |

See [architecture.md](architecture.md#error-handling) for why `TraceId`/`SpanId` aren't always
populated.
