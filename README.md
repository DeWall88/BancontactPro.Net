# BancontactPro.Net

[![NuGet](https://img.shields.io/nuget/v/BancontactPro.Net.svg)](https://www.nuget.org/packages/BancontactPro.Net)
[![License: Polyform Noncommercial](https://img.shields.io/badge/License-Polyform%20NC%201.0-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

A strongly-typed **.NET 10** client library for the [Bancontact Pro Merchant Payment API](https://docs.bancontactpro.com/), Belgium's Bancontact/Payconiq payment network. Targets the online-payment flow (create → hosted checkout or self-rendered QR → webhook confirmation, with a polling fallback) used for webshop-style checkouts.

> **Status: early scaffold, not yet implemented.** This repo currently holds the project
> structure, license, and CI wiring only — no client code exists yet. See
> [docs/research-notes.md](docs/research-notes.md) for what's already known about the API
> ahead of implementation.

---

## Why this exists

No official or actively-maintained .NET SDK exists for the Bancontact Pro API. Packages that
turn up when searching (`CM.Payments.SDK`, `paynl/bancontact-sdk`) are for *other* payment
gateways that merely support Bancontact as one of several payment methods through their own
separate API — not clients for Bancontact Pro itself.

## Planned scope (Payment V3 API)

- **Create payment** — `POST /v3/payments`, returns a payment id (valid 20 minutes), a hosted
  checkout URL, a raw QR-code URL (for a self-rendered checkout page), and a mobile deeplink.
- **Get payment status** — `GET /v3/payments/{id}` — also the recommended polling fallback,
  since callback/redirect ordering isn't guaranteed.
- **Cancel payment** — `DELETE /v3/payments/{id}` — only while `PENDING`/`IDENTIFIED`.
- **Refund** — via the debtor IBAN lookup + refund authority (`MERCHANT_REFUND`), only once a
  payment is `SUCCEEDED`.
- **Request signing** — every API call needs a detached JWS signature (RFC 7797,
  `JWS-Request-Signature-Payment` header) alongside the bearer API key.
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

## Contributing / status tracking

Implementation work is tracked as issues in this repo. See
[docs/research-notes.md](docs/research-notes.md) for the API research this scaffold was built
from.
