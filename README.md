# BancontactPro.Net

[![NuGet](https://img.shields.io/nuget/v/BancontactPro.Net.svg)](https://www.nuget.org/packages/BancontactPro.Net)
[![License: Polyform Noncommercial](https://img.shields.io/badge/License-Polyform%20NC%201.0-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

A strongly-typed **.NET 10** client library for the [Bancontact Pro Merchant Payment API](https://docs.bancontactpro.com/), Belgium's Bancontact/Payconiq payment network. Targets the online-payment flow (create → hosted checkout or self-rendered QR → webhook confirmation, with a polling fallback) used for webshop-style checkouts.

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

**Payment API** (`IPaymentClient`)
- **Create payment** — `POST /v3/payments`, returns a payment id (valid 20 minutes), a hosted
  checkout URL, a raw QR-code URL (for a self-rendered checkout page), and a mobile deeplink.
- **Get payment status** — `GET /v3/payments/{id}` — also the recommended polling fallback,
  since callback/redirect ordering isn't guaranteed.
- **Cancel payment** — `DELETE /v3/payments/{id}` — only while `PENDING`/`IDENTIFIED`.
- **Search payments** — `POST /v3/payments/search`, filtered and paginated.
- **Acknowledge** — `POST /v3/payments/{id}/acknowledge`, for the VOID-mode flow.
- **Debtor refund IBAN** — `GET /v3/payments/{id}/debtor/refundIban`, feeds a manual refund.
- **Static QR (POS)** — `POST /v3/payments/pos`, for in-person/point-of-sale payments.

**Refund API** (`IRefundClient`)
- **Create refund** — `POST /v3/payments/{payment-id}/refunds` (idempotent via an
  `Idempotency-Key` header, requires `MERCHANT_REFUND` authority).
- **Get refund** — `GET /v3/payments/{payment-id}/refunds/{refund-id}`.

**Reconciliation API** (`IReconciliationClient`) — matching bank payouts against the
transactions/refunds behind them, for accounting rather than the customer-facing flow. Data is
only available D+1 09:00 CET.
- **List payouts** — `GET /v3/reconciliation/payouts`
- **List payments in a payout** — `GET /v3/reconciliation/payments`
- **List refunds in a payout** — `GET /v3/reconciliation/refunds`

**Cross-cutting**
- **Request signing** — every API call needs a detached JWS signature (RFC 7797) alongside the
  bearer API key. This is two-directional: verifying webhooks uses Bancontact's JWKS, but
  *signing requests* requires the merchant to host their own JWKS for Bancontact to verify
  against — a real infrastructure requirement, not just a code path.
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

*(Not yet published — this will work once the first release is tagged.)*

## Usage

Configure `BancontactPro` in `appsettings.json` (or another configuration provider — user
secrets/environment variables are strongly recommended for `PrivateKeyPem` and `ApiKey`):

```json
{
  "BancontactPro": {
    "ApiKey": "...",
    "MerchantProfileId": "...",
    "Kid": "...",
    "PrivateKeyPem": "-----BEGIN PRIVATE KEY-----\n...\n-----END PRIVATE KEY-----",
    "Environment": "Preprod"
  }
}
```

Then register the integration and inject the typed clients:

```csharp
builder.Services.AddBancontactProIntegration(builder.Configuration);
```

```csharp
public class CheckoutService(IPaymentClient paymentClient)
{
    public Task<CreatePaymentResponse> CreateAsync(long amountInCents) =>
        paymentClient.CreateAsync(new CreatePaymentRequest(amountInCents));
}
```

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

`PrivateKeyPem`/`Kid` are the merchant's *own* signing key — Bancontact must be given the
corresponding public JWK (see `JwkExporter.ToPublicJwkJson`) during onboarding, hosted at
whatever JWKS URL the merchant registers. This library doesn't host that endpoint itself.

## Contributing / status tracking

Implementation work is tracked as issues in this repo. See
[docs/research-notes.md](docs/research-notes.md) for the API research this scaffold was built
from.

## Development

Built with AI pair-programming assistance from Claude Sonnet 5 (Anthropic) — commits are
co-authored accordingly.
