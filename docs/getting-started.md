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
  [Payout & remittance](https://docs.bancontactpro.com/guides/general/payoutremittance052025),
  [Payment link update](https://docs.bancontactpro.com/guides/general/payloadurlupdate) (the
  Payconiq→Bancontact Pro domain migration — relevant to step 12).

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
   weeks** turnaround. They'll issue a Product Profile ID (PPID) and API key per integration type:
   each of the four product types gets its own pair. Step 4 shows where each one goes.
2. **Production**: apply via the merchant portal once pre-production is verified working (the
   application portal is separate from the [developer docs](https://docs.bancontactpro.com/) —
   devsupport will point you to it).

If you'll use refunds or reconciliation, also generate your own signing key pair (step 2) and
share the URL of the JWKS hosting its public half with Bancontact; creating and checking
payments doesn't need it. And install the Bancontact Pay test app (see
[Testing with the Bancontact Pay app](#testing-with-the-bancontact-pay-app) near the end of this
guide) so you can actually confirm a payment end-to-end once you're wired up.

## 2. Generate a signing key pair (Refund and Reconciliation only)

**Skip this step if you only create payments.** The Payment API authenticates with the API key
alone: its spec lists the API key and a JWS signature as *alternative* security requirements.
The Refund and Reconciliation APIs accept nothing but a signature, which Bancontact verifies
using ES256 (ECDSA on the P-256 curve) against a JWKS you host. Generate a key pair with OpenSSL:

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

Bancontact issues a separate **API key and PPID per product type**, so credentials are
configured per product. The product names are `OnDisplay`, `OnReceipt`, `StaticQr`, `TopUp`
and `Invoice`. Configure only the ones you use. For payments, this is all you need:

```json
{
  "BancontactPro": {
    "Environment": "Preprod",
    "Products": {
      "StaticQr":  { "ApiKey": "Static QR's API key",  "MerchantProfileId": "Static QR's PPID" },
      "OnDisplay": { "ApiKey": "On Display's API key", "MerchantProfileId": "On Display's PPID" }
    }
  }
}
```

Add your own signing key (step 2) once you use refunds or reconciliation. It is shared by all
products:

```json
{
  "BancontactPro": {
    "Kid": "your-chosen-key-id",
    "PrivateKeyPem": "-----BEGIN PRIVATE KEY-----\n...\n-----END PRIVATE KEY-----"
  }
}
```

Where each credential from Bancontact's onboarding email goes:

| The email says | Setting | Required? |
|---|---|---|
| "A unique **API key**" | `Products:<Product>:ApiKey` | For every product you use |
| "A unique **Product Profile Identifier** or PPID" | `Products:<Product>:MerchantProfileId` | For every product you use, always as a pair with that product's API key. It is also the `sub` claim of signed requests. |
| "… and/or **JWS key**" | none | Nothing to fill in. Bancontact doesn't issue you a private key, and callbacks are verified against Bancontact's public JWKS, which the library fetches itself. If you do receive something labelled "JWS key", ask devsupport what it is. |
| *(your own key pair, step 2)* | `Kid` + `PrivateKeyPem` | Only for Refund and Reconciliation clients |

Always take a product's API key and PPID from the **same** product's entry in the email. A
product with only one of the two fails validation at startup; a product with neither is simply
not available.

Keep the API keys and `PrivateKeyPem` out of source control — use `dotnet user-secrets` locally
(or environment variables such as `BancontactPro__Products__StaticQr__ApiKey` in a container):

```bash
dotnet user-secrets set "BancontactPro:Products:StaticQr:ApiKey" "..."
dotnet user-secrets set "BancontactPro:Products:StaticQr:MerchantProfileId" "..."
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

This single call registers everything: the JWKS client, the callback verifier, and
`IBancontactClientFactory`, which hands out clients bound to one product:

```csharp
IPaymentClient paymentClient = clients.Payments(BancontactProduct.OnDisplay);               // sends that product's API key
IRefundClient refundClient = clients.Refunds(BancontactProduct.OnDisplay);                  // signed, with that product's PPID
IReconciliationClient reconciliationClient = clients.Reconciliation(BancontactProduct.OnDisplay);
```

The snippets below use those three variables. A payment belongs to the product it was created
with, so fetch, cancel and refund it with clients for that same product.

Configuration is validated on first access: no product configured, a product with only half its
pair, half a signing key (`Kid` without `PrivateKeyPem` or the other way round), or an
unparseable `PrivateKeyPem` throws `OptionsValidationException` with a specific message rather
than failing confusingly later on the first real API call. Asking for a client of a product
without a complete pair throws `BancontactProductNotConfiguredException`; asking for a Refund or
Reconciliation client without a signing key throws `BancontactSigningKeyNotConfiguredException`.
Both say which settings are missing, and nothing is sent to Bancontact.

## Which product uses which call

The library wraps the API one-to-one and doesn't decide which product uses which call: Bancontact's
product guides do. In short:

| Product | How a payment starts | Library call | How you learn the outcome |
|---|---|---|---|
| [On a Display](https://docs.bancontactpro.com/guides/instore/ondisplay052025v4) | One payment per sale, its QR code shown on a screen; valid **120 seconds** | `clients.Payments(OnDisplay).CreateAsync` → `POST /v3/payments` | Callback; `GetAsync` as the fallback |
| [Static QR](https://docs.bancontactpro.com/guides/instore/staticqr052025v4) | A sticker per till, printed once (`PaymentLinkBuilder.BuildStaticQrLocationUrl`); per sale, a payment created just before the customer scans; valid **120 seconds** | `clients.Payments(StaticQr).CreateStaticQrPaymentAsync` → `POST /v3/payments/pos` with the till's `posId` | Callback; `GetAsync` as the fallback |
| [On a Receipt](https://docs.bancontactpro.com/guides/instore/receipt052025v4) | A QR code printed on the receipt, built locally | None: `PaymentLinkBuilder.BuildFixedAmountPaymentUrl` | Callback to the URL configured on the product; the payment ID first appears there |
| [Top Up](https://docs.bancontactpro.com/guides/online/topup052025v4) | Same as On a Receipt. Top Up needs a callback URL configured on the product by Bancontact | None: `PaymentLinkBuilder.BuildFixedAmountPaymentUrl` | Same as On a Receipt |
| [Invoice](https://docs.bancontactpro.com/guides/invoice/invoice052025v4) | Same as On a Receipt; the guide says Bancontact no longer offers it directly | None: `PaymentLinkBuilder.BuildFixedAmountPaymentUrl` | Same as On a Receipt |

For every product, `GetAsync`, `SearchAsync`, refunds and reconciliation use the clients of the
product the payment belongs to. For URL-based products, each combination of amount, description
and reference must be unique, or the QR code can be treated as already paid.

The [sample API](../samples/BancontactPro.Net.Sample.Api/README.md) exposes every call over HTTP,
one route per library method, for trying these flows against preprod.

## 6. Create a payment

Corresponds to `POST /v3/payments` in the
[Payment API spec](https://docs.bancontactpro.com/_bundle/apis/merchant-payment.openapi.json) —
the same operation regardless of which in-store product (On a Display, On a Receipt, Static QR)
your merchant profile is configured for; what differs per product is the payment's validity
window and which `_links` field you're expected to render.

```csharp
public class PaymentService(IBancontactClientFactory clients)
{
    public async Task<string> CreateAsync(long amountInCents, string orderReference)
    {
        var paymentClient = clients.Payments(BancontactProduct.OnDisplay);

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
var payment = await clients.Payments(BancontactProduct.StaticQr).CreateStaticQrPaymentAsync(
    new CreateStaticQrPaymentRequest(Amount: 1000, PosId: "till-1", ShopName: "My Shop"));
```

Creating a new static QR payment for the same `PosId`/profile combination invalidates any
existing active one for that POS — this is by design (it's how a physical, reusable QR code at
a till gets "refreshed" for the next customer), not something to guard against client-side. The
*printed* QR code itself is a separate, one-time thing — see step 12.

## 12. Print a QR code without calling the API (On a Receipt, Top Up, Static QR setup)

On a Receipt, Top Up, and Static QR's printed code are all built **client-side** — there's no
REST call that returns them. See
[architecture.md](architecture.md#qr-and-deeplink-url-templating-on-a-receipt-top-up-static-qr)
for why this is structurally different from steps 6 and 11 above.

Every `PaymentLinkBuilder` method takes the environment to build for. Pass the configured
`BancontactProOptions.Environment` (from `IOptions<BancontactProOptions>`), so the QR codes match
the API you call:

| Environment | Payload host | QR image generator |
|---|---|---|
| `Production` | `https://pay.bancontact.net` | `https://qrcodegenerator.api.bancontact.net` |
| `Preprod` | `https://pay.acc.bancontact.net` | `https://qrcodegenerator.api.preprod.bancontact.net` |

The guides only show the production hosts. The preprod ones are those in the links Bancontact's
own preprod API returns (see [research-notes.md](research-notes.md)). The production generator
answers 400 for a `pay.acc` payload, so build the payload and the image URL for the same
environment.

For On a Receipt or Top Up (a fresh QR per transaction, amount baked in):

```csharp
var environment = options.Value.Environment; // IOptions<BancontactProOptions>

var payload = PaymentLinkBuilder.BuildFixedAmountPaymentUrl(
    environment,
    productProfileId: "your-ppid",
    amountCents: 250,
    description: "Coffee",
    reference: orderReference);

var qrImageUrl = PaymentLinkBuilder.BuildQrCodeImageUrl(environment, payload, QrCodeImageFormat.PNG, QrCodeImageSize.M);
```

Omit `amountCents` for Top Up's "open value" QR type (the payer enters the amount themselves) —
whether a given Top Up product is open-value, modifiable, or fixed is configured by devsupport
at product creation, not something this call controls.

For Static QR's printed code (a one-time setup step, separate from
`CreateStaticQrPaymentAsync` in step 11, which attaches the actual amount per transaction):

```csharp
var payload = PaymentLinkBuilder.BuildStaticQrLocationUrl(environment, productProfileId: "your-ppid", posId: "till-1");
var qrImageUrl = PaymentLinkBuilder.BuildQrCodeImageUrl(environment, payload, QrCodeImageFormat.SVG);
```

Print `qrImageUrl` once; from then on, each sale at that till calls `CreateStaticQrPaymentAsync`
with the same `posId` to attach a fresh amount to that already-printed code.

## 13. Reconciliation (accounting, not the checkout flow)

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
| `BancontactApiException` | any client method | Bancontact returned a non-success HTTP status. Carries `StatusCode`, `Code`, `Message`, and (Payment API only) `TraceId`/`SpanId` for support requests. A body that isn't Bancontact's `{code, message}` shape (e.g. a gateway's 401) is kept verbatim in `Message`. |
| `RefundIdempotencyConflictException` | `IRefundClient.CreateAsync` | The `Idempotency-Key` was reused with different refund parameters — a caller bug, not transient. |
| `InvalidCallbackException` | `BancontactCallbackVerifier.VerifyAndParseAsync` | The callback's signature didn't verify, or its body didn't match the expected schema. |
| `OptionsValidationException` | first access of `BancontactProOptions` | No product is configured, a product has only half its API key and PPID pair, only half of the signing key (`Kid`/`PrivateKeyPem`) is set, or `PrivateKeyPem` isn't a parseable PEM-encoded EC key. |
| `BancontactProductNotConfiguredException` | `IBancontactClientFactory.Payments`/`Refunds`/`Reconciliation` | The product has no complete API key and PPID pair. `Product` says which one. |
| `BancontactSigningKeyNotConfiguredException` | `IBancontactClientFactory.Refunds`/`Reconciliation` | No signing key is configured. Those APIs accept only signed requests. |

### 401 Unauthorized from Bancontact

Bancontact doesn't recognise the API key. In order of likelihood:

1. **The API key and the PPID are swapped.** They look alike. In a successful create-payment
   response the PPID comes back as `creditor.profileId`, which tells you which of the two values is
   the PPID.
2. **The key belongs to another product or environment.** Each product has its own key, and
   preprod and production keys differ.
3. **The key was copied with hidden characters** (a non-breaking or zero-width space from an
   HTML email).

To test a key on its own, ask for a payment that doesn't exist. The key is fine if the answer is
404 `PAYMENT_NOT_FOUND`, and wrong if it is 401:

```bash
curl -s -o /dev/null -w '%{http_code}\n' -H 'Authorization: Bearer <API key>' https://merchant.api.preprod.bancontact.net/v3/payments/probe
```

See [architecture.md](architecture.md#error-handling) for why `TraceId`/`SpanId` aren't always
populated.
