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
> [docs/getting-started.md](https://github.com/DeWall88/BancontactPro.Net/blob/main/docs/getting-started.md)
> for details.
>
> **Status: functionally complete, not yet verified end-to-end against Bancontact.** Request
> signing, webhook verification, and all three typed API clients (Payment, Refund,
> Reconciliation) are implemented and unit-tested, with DI registration wired up. What's
> missing is a real run against Bancontact's pre-production environment — see
> [docs/research-notes.md](https://github.com/DeWall88/BancontactPro.Net/blob/main/docs/research-notes.md)
> for open questions and current status.

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

The quick version below covers the basics. For onboarding, generating a signing key pair,
handling webhooks, refunds, and reconciliation in detail, see
[docs/getting-started.md](https://github.com/DeWall88/BancontactPro.Net/blob/main/docs/getting-started.md).
For how the signing/verification internals and DI wiring actually work, see
[docs/architecture.md](https://github.com/DeWall88/BancontactPro.Net/blob/main/docs/architecture.md).

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

Implementation work is tracked as issues on
[GitHub](https://github.com/DeWall88/BancontactPro.Net/issues). See
[docs/research-notes.md](https://github.com/DeWall88/BancontactPro.Net/blob/main/docs/research-notes.md)
for the API research this library was built from.

## Development

Built with AI pair-programming assistance from Claude Sonnet 5 (Anthropic) — commits are
co-authored accordingly.
