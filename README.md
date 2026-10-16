# BancontactPro.Net

[![NuGet](https://img.shields.io/nuget/v/BancontactPro.Net.svg)](https://www.nuget.org/packages/BancontactPro.Net)
[![License: Polyform Noncommercial](https://img.shields.io/badge/License-Polyform%20NC%201.0-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

A strongly-typed **.NET 10** client library for [Bancontact Pro](https://docs.bancontactpro.com/)'s Merchant Payment/Refund/Reconciliation APIs, Belgium's Bancontact/Payconiq payment network. Targets Bancontact's in-store QR products — [On a Display](https://docs.bancontactpro.com/guides/instore/ondisplay052025v4), [On a Receipt](https://docs.bancontactpro.com/guides/instore/receipt052025v4), [Static QR](https://docs.bancontactpro.com/guides/instore/staticqr052025v4), and [Top Up](https://docs.bancontactpro.com/guides/online/topup052025v4) — where the merchant backend creates a payment, renders the resulting QR code (or deep-links to the payer's banking app), and confirms the outcome via a signed webhook with a polling fallback.

> Bancontact's separate **Online Sales** product (a hosted-checkout/redirect flow more typical of
> webshop checkouts) is marked by Bancontact as
> ["no longer offered directly"](https://docs.bancontactpro.com/guides/online/onlinesales) as of
> this writing — it isn't the flow this library is scoped around. The underlying Payment API
> operations (create/get/cancel/search) are identical either way; only the product configuration,
> payment expiry window, and recommended rendering differ. See
> [docs/getting-started.md](docs/getting-started.md) for details.
>
> **Status: functionally complete, not yet verified end-to-end against Bancontact.** Request
> signing, webhook verification, and all three typed API clients (Payment, Refund,
> Reconciliation) are implemented and unit-tested, with DI registration wired up. What's
> missing is a real run against Bancontact's pre-production environment — see
> [docs/research-notes.md](docs/research-notes.md) for open questions and current status.

---

## Why this exists

No official or actively-maintained .NET SDK exists for the Bancontact Pro API. Packages that
turn up when searching (`CM.Payments.SDK`, `paynl/bancontact-sdk`) are for *other* payment
gateways that merely support Bancontact as one of several payment methods through their own
separate API — not clients for Bancontact Pro itself.

## Scope

**Payment API** (`IPaymentClient`, [spec](https://docs.bancontactpro.com/_bundle/apis/merchant-payment.openapi.json))
- **Create payment** — `POST /v3/payments`, returns a payment id, a raw QR-code URL, and a
  mobile deeplink (plus a hosted checkout URL, mainly relevant to the discontinued Online Sales
  product). The payment's validity window is set by your merchant profile's configured product,
  not a fixed constant — e.g. **2 minutes** for On a Display, historically 20 minutes for
  Online Sales.
- **Get payment status** — `GET /v3/payments/{id}` — also the recommended polling fallback,
  since callback/redirect ordering isn't guaranteed.
- **Cancel payment** — `DELETE /v3/payments/{id}` — only while `PENDING`/`IDENTIFIED`.
- **Search payments** — `POST /v3/payments/search`, filtered and paginated.
- **Acknowledge** — `POST /v3/payments/{id}/acknowledge`, for the VOID-mode flow.
- **Debtor refund IBAN** — `GET /v3/payments/{id}/debtor/refundIban`, feeds a manual refund.
- **Static QR (POS)** — `POST /v3/payments/pos`, for in-person/point-of-sale payments.

**Refund API** (`IRefundClient`, [spec](https://docs.bancontactpro.com/_bundle/apis/refund-public.openapi.json))
- **Create refund** — `POST /v3/payments/{payment-id}/refunds` (idempotent via an
  `Idempotency-Key` header, requires `MERCHANT_REFUND` authority).
- **Get refund** — `GET /v3/payments/{payment-id}/refunds/{refund-id}`.

**Reconciliation API** (`IReconciliationClient`, [spec](https://docs.bancontactpro.com/_bundle/apis/merchant-reconciliation.openapi.json)) — matching bank payouts against the
transactions/refunds behind them, for accounting rather than the customer-facing flow. Data is
only available D+1 09:00 CET.
- **List payouts** — `GET /v3/reconciliation/payouts`
- **List payments in a payout** — `GET /v3/reconciliation/payments`
- **List refunds in a payout** — `GET /v3/reconciliation/refunds`

**Cross-cutting**
- **Authentication** — the Payment API needs only the bearer API key (its spec lists the API
  key and a JWS signature as alternatives). The Refund and Reconciliation APIs accept nothing
  but a detached JWS signature (RFC 7797), which Bancontact verifies against a JWKS the
  merchant hosts. That is a real infrastructure requirement, so the signing key is optional and
  only needed once you use those two APIs.
- **Webhook verification** — callbacks are JWS-signed (ES256) against a rotating JWKS
  (`jwks.bancontact.net` prod / `jwks.preprod.bancontact.net` preprod), not a shared-secret
  HMAC. Retried for up to 24h on non-200/timeout; each callback carries a unique `jti`.

## Requirements

- .NET 10 or later
- A Bancontact Pro merchant account (onboarding is a formal application process, not
  self-serve — see [devsupport@bancontact.com](mailto:devsupport@bancontact.com))

## Installation

```bash
dotnet add package BancontactPro.Net
```

Preview releases need `--prerelease` (`dotnet add package BancontactPro.Net --prerelease`). The package depends only on `Microsoft.Extensions.*`, so it works in ASP.NET Core apps, worker services and console apps alike.

## Usage

The quick version below covers the basics. For onboarding, generating a signing key pair,
handling webhooks, refunds, and reconciliation in detail, see
[docs/getting-started.md](docs/getting-started.md). For how the signing/verification internals
and DI wiring actually work, see [docs/architecture.md](docs/architecture.md).

Configure `BancontactPro` in `appsettings.json` (or another configuration provider — user
secrets/environment variables are strongly recommended for `ApiKey` and `PrivateKeyPem`).
For payments, the API key is all you need:

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

Bancontact issues a separate **API key and PPID per product type**, so credentials are
configured per product: `OnDisplay`, `OnReceipt`, `StaticQr`, `TopUp` or `Invoice`. Configure
only the products you use. Where each item of Bancontact's onboarding email goes:

| Bancontact's onboarding email says | Setting | Required? |
|---|---|---|
| "A unique API key" | `Products:<Product>:ApiKey` | For every product you use |
| "A unique Product Profile Identifier or PPID" | `Products:<Product>:MerchantProfileId` | For every product you use: always as a pair with that product's API key |
| "… and/or JWS key" | none | Nothing to fill in: Bancontact doesn't issue you a private key |
| *(your own, for Refund/Reconciliation)* | `Kid` + `PrivateKeyPem` | Only for Refund and Reconciliation clients; shared by all products |

Then register the integration and ask `IBancontactClientFactory` for a client per product. There are
two ways to register, over the same core:

| | `AddBancontactProIntegration(configuration)` | `AddBancontactPro<TProvider>()` |
|---|---|---|
| Use it when | The settings are static: appsettings, environment variables, user secrets | The host keeps its credentials somewhere that changes at runtime (a database, an admin page), or must start with none |
| Credentials come from | `BancontactProOptions`, bound once from the `BancontactPro` section | Your `IBancontactCredentialsProvider`, asked on every call |
| At startup | Validated: the host does not start without a configured product | Nothing is read or validated: with nothing configured the host starts, and clients report "not configured" when used |
| A change applies | After a restart | To the next call |

```csharp
builder.Services.AddBancontactProIntegration(builder.Configuration);   // static settings
// or
builder.Services.AddBancontactPro<MyCredentialsProvider>();            // credentials that can change
```

```csharp
public class CheckoutService(IBancontactClientFactory clients)
{
    public async Task<CreatePaymentResponse> CreateAsync(long amountInCents)
    {
        var payments = await clients.PaymentsAsync(BancontactProduct.OnDisplay);
        return await payments.CreateAsync(new CreatePaymentRequest(amountInCents));
    }
}
```

A call is only possible for a product with a complete pair. `clients.PaymentsAsync(product)`,
`RefundsAsync(product)` and `ReconciliationAsync(product)` throw
`BancontactProductNotConfiguredException` for a product without one, and the Refund and
Reconciliation clients throw `BancontactSigningKeyNotConfiguredException` without a signing key.
Nothing is sent to Bancontact in either case. Fetch, cancel and refund a payment with the same
product it was created with. Ask the factory for a client per operation instead of holding on to
one: a client keeps the credentials and environment it was created with.

A provider returns a `BancontactCredentials` (environment, API key and PPID per product, optional
signing key). `ConfigurationCredentialsProvider`, the one behind `AddBancontactProIntegration`,
shows how to build it. See [docs/getting-started.md](docs/getting-started.md#5-register-the-integration)
for a provider backed by a database.

Verifying an inbound webhook callback:

```csharp
app.MapPost("/bancontact/callback", async (HttpRequest request, BancontactCallbackVerifier verifier) =>
{
    using var reader = new StreamReader(request.Body);
    var body = System.Text.Encoding.UTF8.GetBytes(await reader.ReadToEndAsync());
    var signature = request.Headers["Signature"].ToString();

    var result = await verifier.VerifyAndParseAsync(signature, body, request.Path);
    // Dedupe using result.RequestId (jti) -- Bancontact retries callbacks for up to 24h.

    return Results.Ok();
});
```

`PrivateKeyPem`/`Kid` are the merchant's *own* signing key, needed only for Refund and
Reconciliation. Per those specs, the merchant hosts the public half as a JWKS (see
`JwkExporter.ToPublicJwkJson`) and shares the URL with Bancontact during integration. This
library doesn't host that endpoint itself.

## Contributing / status tracking

Implementation work is tracked as issues in this repo. See
[docs/research-notes.md](docs/research-notes.md) for the API research this scaffold was built
from.

## Development

Built with AI pair-programming assistance from Claude Sonnet 5 (Anthropic) — commits are
co-authored accordingly.

## License

BancontactPro.Net is free for **noncommercial** use under the [PolyForm Noncommercial License 1.0.0](LICENSE):
personal projects, study, hobby and amateur use, and use by charitable, educational, public
research, public safety or health, environmental protection and government organizations.

**Commercial use**, for example taking payments for a business or building the library into a
product or service you sell, needs a separate commercial license. To ask for one, open an issue on
[GitHub](https://github.com/DeWall88/BancontactPro.Net/issues) or contact
[@DeWall88](https://github.com/DeWall88).

Required Notice: Copyright (c) 2026 DeWall88 (https://github.com/DeWall88/BancontactPro.Net)
