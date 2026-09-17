# Research notes

Captured before any implementation started, from the [Bancontact Pro developer
portal](https://docs.bancontactpro.com/) (getting-started guide, Payment V3 API reference,
callback guide, online-sales guide). Notes below are from AI-summarized doc excerpts, not the
raw OpenAPI spec — good enough to scope the work, **not** precise enough to code the exact
request/response DTOs against. Re-read the raw OpenAPI YAML directly before implementing.

## What Bancontact Pro is

A rebrand of the former Payconiq platform. Product lineup centers on QR/bank-app payments
across four categories: On a Display, On a Receipt, Static QR, and Top Up (a closed-loop
card/wristband credit system). A genuine online/e-commerce flow exists separately (see below)
— not just the in-person QR products.

## Online payment flow

1. Merchant backend `POST`s to create a payment.
2. Customer is redirected to Bancontact's hosted checkout page, **or** the merchant renders
   its own checkout using the returned QR-code URL directly.
3. Customer confirms in their banking app.
4. Bancontact sends an asynchronous webhook to the merchant's `CallbackUrl` with status
   `PENDING` / `AUTHORIZED` / `FAILED`.
5. Customer is redirected back to the merchant's `ReturnUrl`.

**Callback/redirect ordering is not guaranteed** — the docs explicitly call this out, which is
why `GET /v3/payments/{id}` exists as a required fallback, not an optional nicety.

## Endpoints (Payment V3 API)

| Action | Method | Path | Notes |
|---|---|---|---|
| Create payment | `POST` | `/v3/payments` | Response: payment id (valid 20 min), `_links.checkout.href` (hosted page), `_links.self.href` (QR code URL, accepts `f=SVG\|PNG` and `s=S\|M\|L\|XL` params), `_links.deeplink.href` (mobile) |
| Get payment | `GET` | `/v3/payments/{id}` | Polling fallback. Errors: 401 `UNAUTHORIZED`, 403 `ACCESS_DENIED`, 404 `PAYMENT_NOT_FOUND` |
| List payments | `GET` | `/v3/payments` | Filtered, paginated |
| Cancel payment | `DELETE` | `/v3/payments/{id}` | Only while `PENDING`/`IDENTIFIED` — 422 `PAYMENT_NOT_PENDING` otherwise |

Required create-payment fields (minimum): amount, currency, `CallbackUrl`, `ReturnUrl`.
Optional: description, order reference (SEPA character-set restrictions apply to both).

Payment status values seen: `PENDING`, `IDENTIFIED`, `SUCCEEDED`, `CANCELLED` (Payment API
reference) — the online-sales guide separately mentions `PENDING`/`AUTHORIZED`/`FAILED` for
webhook payloads specifically; reconcile these against the raw OpenAPI spec, they may be two
different status vocabularies (payment resource vs. webhook event).

## Endpoints (Refund API)

| Action | Method | Path | Notes |
|---|---|---|---|
| Create refund | `POST` | `/v3/payments/{payment-id}/refunds` | Idempotent via an `Idempotency-Key` header. Requires `MERCHANT_REFUND` authority. |
| Get refund | `GET` | `/v3/payments/{payment-id}/refunds/{refund-id}` | |

Refunds are scoped under the payment they refund (not a top-level resource) — depends on the
Payment API client existing first. `PROCESSING` status and `DEBTOR_IBAN_NOT_AVAILABLE` error
code were both **removed** in API v3.0.2 per the changelog note in the spec — don't model
either. Exact request/response schema (partial vs. full refund support, minimum amount, time
window after the original payment) not yet confirmed from the excerpts gathered — get this
from the raw OpenAPI spec.

## Endpoints (Reconciliation API)

Solves a different problem than the two above: matching bank **payouts** (settlement batches)
against the individual transactions/refunds that make them up — for accounting reconciliation,
not for the customer-facing payment flow.

| Action | Method | Path | Notes |
|---|---|---|---|
| List payouts | `GET` | `/v3/reconciliation/payouts` | By `date` (`YYYY-MM-DD`). Returns settlement batches: payout id, merchant id, IBAN, status (`SUCCEEDED`/`FAILED`), payout date, currency, transaction counts, net amount in cents. |
| List payments in a payout | `GET` | `/v3/reconciliation/payments` | By `payout-id` or `start-date`/`end-date` (max 30-day range). Only `SUCCEEDED` transactions. Per-record: payment id, merchant name, channel (`ONLINE`/`INSTORE`/`INVOICE`), amount (cents), reference, description, timestamp. |
| List refunds in a payout | `GET` | `/v3/reconciliation/refunds` | Same shape/filters as payments, above. Only `SUCCEEDED` refunds. |

- Pagination: `size` (default 10000), `page` (default 0) on all three.
- **Data availability: D+1 starting at 09:00 CET** — not real-time, don't design around
  polling this frequently or expecting same-day data.
- JSON only, no CSV/file export.
- Exists in both PREPROD and PROD.

## Authentication

- Bearer API key in the `Authorization` header for every call.
- **Plus** a detached JWS request signature (RFC 7797). Header name seen as
  `JWS-Request-Signature-Payment` in the Payment API docs and plain `Signature` in the Refund
  API docs — confirm whether this is a real per-API-family difference or just inconsistent
  documentation before assuming either is authoritative.
- The JOSE header for request signing includes merchant profile ID, timestamp, a unique
  request identifier, and the request path.
- **Important, easy to miss**: this is a *two-directional* JWKS relationship, not one.
  Verifying *webhooks* uses Bancontact's JWKS (see below). Signing *requests* is the other way
  around — **the merchant must host their own public key(s) in JWKS format and share that URL
  with Bancontact** during onboarding, so Bancontact can verify the merchant's request
  signatures. This means the library (or its consumer) needs a way to publish a JWKS endpoint,
  not just sign outgoing requests — a real infrastructure requirement, not just a code path.
- Auth model terms seen in the API: `subjectType` (`INTEGRATOR`/`MERCHANT`), `resource`
  (`PAYMENTPROFILE`), `authority` (`MERCHANT_PAYMENT`/`MERCHANT_REFUND`) — suggests a
  permission-scoped design, possibly aimed as much at PSPs/ISVs integrating on behalf of many
  merchants as at a single merchant integrating directly.

## Webhook verification

- JWS-signed, **ES256** asymmetric signing — no shared-secret/HMAC option.
- Verify against a JWKS fetched from `jwks.bancontact.net` (prod) /
  `jwks.preprod.bancontact.net` (preprod): extract `kid` from the JOSE header, find the
  matching JWK, verify; if no match, refresh the JWKS cache and retry once.
- Headers sent: `signature` (the JWS), `content-type: application/json`,
  `user-agent: Bancontact Payments/v3`.
- Retries for up to 24h if the merchant doesn't return HTTP 200 within 15s, or returns
  429/500/503/504/509.
- Each callback carries a unique `jti` in the JOSE header — use it for idempotency tracking
  (duplicate callbacks for the same payment/status are expected, not a bug).

## Onboarding

Not self-serve. Production: apply via the merchant portal. Pre-production (sandbox): email
devsupport@bancontact.com with company name, Merchant ID, and contact details — **up to two
weeks** turnaround. No test-card/QR-simulation details were found in the docs excerpts
gathered so far.

## Why build this as a separate library rather than inline in a consuming app

Mirrors the [`Eventbrite.Net`](https://github.com/DeWall88/Eventbrite.Net) pattern: a
general-purpose, non-app-specific API client (including the JWS signing/verification, which is
genuinely reusable logic) belongs in its own package, consumed via `PackageReference` by
whatever application needs it — not duplicated inline.

## Alternative considered

If *Bancontact the payment method* (rather than *Bancontact Pro the platform specifically*) is
ever the actual requirement, [Mollie](https://www.nuget.org/packages/Mollie.Api) has a mature
official .NET SDK with Bancontact support and a meaningfully simpler auth model (bearer key +
webhook secret, no JWS/JWKS). Noted here as a known trade-off, not a recommendation to switch.
