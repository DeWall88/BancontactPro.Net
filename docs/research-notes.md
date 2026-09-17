# Research notes

Original pass (below, "v1") was built from AI-summarized doc excerpts. **v2 (this update)** is
sourced directly from the raw OpenAPI specs, downloaded from the developer portal:

- Payment API: `https://docs.bancontactpro.com/_bundle/apis/merchant-payment.openapi.json` — v3.6.5
- Refund API: `https://docs.bancontactpro.com/_bundle/apis/refund-public.openapi.json` — v3.0.3
- Reconciliation API: `https://docs.bancontactpro.com/_bundle/apis/merchant-reconciliation.openapi.json` — v3.0.1

Servers: `https://merchant.api.preprod.bancontact.net` (PREPROD), `https://merchant.api.bancontact.net` (PROD) — same servers for all three APIs.

This section resolves the open questions from v1 and corrects a few wrong assumptions. Anything
not explicitly called out below as corrected still stands from v1.

## Resolved: request signing header

**One real answer for all three APIs: the HTTP header is literally `Signature`.**

`JWS-Request-Signature-Payment` / `JWS-Request-Signature-Refund` / `JWS-Request-Signature` are
just the **OpenAPI security scheme names** in each spec's `components.securitySchemes` — not
header names. All three schemes declare `"name": "Signature", "in": "header", "type": "apiKey"`.
The v1 notes conflated the scheme name with the header name; there was never a real
per-API-family header difference.

Bearer API key is a **separate** header/scheme (`Authorization`, scheme `api_key_payment_profile`
— only present on the Payment API spec; Refund/Reconciliation specs only declare the signature
scheme, but the getting-started guide confirms the API key is required on every call regardless).

Signature computation (identical across all three specs):

```text
jws = base64url(JOSE Header) + "." + ES256(base64url(JOSE Header) + "." + base64url(Body))
```

(detached JWS per RFC 7797 — body isn't embedded in the compact serialization, but is signed over as `base64url(header).base64url(body)`)

JOSE header:

```json
{
  "typ": "jose+json",
  "kid": "<JWK kid>",
  "alg": "ES256",
  "https://payconiq.com/sub": "{merchantProfileId}",
  "https://payconiq.com/iss": "Payconiq",
  "https://payconiq.com/iat": "{ISO 8601 UTC timestamp, e.g. 2026-01-01T00:00:00.000Z}",
  "https://payconiq.com/jti": "{unique request id}",
  "https://payconiq.com/path": "{request path, e.g. /v3/payments/{payment-id}}",
  "crit": ["https://payconiq.com/sub", "https://payconiq.com/iss", "https://payconiq.com/iat", "https://payconiq.com/jti", "https://payconiq.com/path"]
}
```

**One odd inconsistency preserved as-is from the spec, not resolved**: the Reconciliation API's
scheme description sets `iss` to `"{Merchant Id}"` (templated — implies the merchant's own ID),
while Payment and Refund both hardcode `iss: "Payconiq"` literally. Possibly a copy/paste error
in the reconciliation spec's prose description (it's describing the same claim set almost
verbatim otherwise). Worth a clarifying question to devsupport before finalizing #2, since
getting `iss` wrong would make every signature invalid.

The Payment spec's scheme description also literally says "signature of **response** payload"
and "JWS Payload MUST be the same as **response** body" (Refund/Reconciliation both say
**request**). This makes sense once you see it's reused for the `/callback` operation too (see
below) — but double-check this isn't a real distinction before assuming it's copy/paste.

**Signing direction is genuinely bidirectional and confirmed at the operation level**: every
merchant→Bancontact call requires `[api_key_payment_profile, JWS-Request-Signature-Payment]`
(both). The inbound `POST /callback` (Bancontact→merchant) requires
`[JWS-Request-Signature-Payment]` **alone** (no API key — makes sense, it's not the merchant
authenticating to Bancontact, it's Bancontact signing what it sends). Same header name
(`Signature`) both directions; different keys (merchant's key when merchant signs a request,
Bancontact's key — verified via JWKS — when Bancontact signs a callback).

**Also per the Refund/Reconciliation scheme descriptions**: the merchant/partner must host its
own public key as JWKS and share the URL with Bancontact during integration — confirms the
"two-directional JWKS" infrastructure requirement flagged in v1.

## Resolved: payment status vocabulary — it's one enum, not two

v1 flagged a *possible* split between a "payment resource" vocabulary and a "webhook event"
vocabulary. **Resolved: there is exactly one canonical enum** (`merchant-payment-status`), used
identically in the payment resource, the search response, and the webhook callback body:

```
PENDING, IDENTIFIED, AUTHORIZED, AUTHORIZATION_FAILED, SUCCEEDED, FAILED, CANCELLED, EXPIRED,
PENDING_MERCHANT_ACKNOWLEDGEMENT, VOIDED
```

(Note the correct spelling is `PENDING_MERCHANT_ACKNOWLEDGEMENT` — a prose guide page had it as
`..._AKNOWLEDGMENT`, but the literal spec enum is `ACKNOWLEDGEMENT`.)

Per-status meaning, from the spec description:

| Status | Meaning |
|---|---|
| `PENDING` | Payment created, awaiting identify step |
| `IDENTIFIED` | User scanned the QR / opened in-app |
| `AUTHORIZED` | User confirmed, bank authorized |
| `AUTHORIZATION_FAILED` | Bank-side authorization failed (final) |
| `SUCCEEDED` | Completed (final) |
| `FAILED` | Something else went wrong (final) |
| `CANCELLED` | Cancelled by user or merchant (final) |
| `EXPIRED` | Not completed in time (final) |
| `PENDING_MERCHANT_ACKNOWLEDGEMENT` | Awaiting merchant ack, VOID-mode flow only |
| `VOIDED` | Cancelled after consumer confirmation, VOID-mode flow only (final) |

Note also: the create-payment response's `status` field is typed as an enum containing only
`PENDING` (i.e. it's always `PENDING` right after creation, modeled as a distinct one-value enum
in the spec rather than reusing the full enum) — same pattern on the refund side (see below).

## Corrected: the Payment API surface is bigger than issue #3 assumed

v1/issue #3 assumed 4 operations (create/get/list/cancel) with list as `GET /v3/payments`. The
**real spec has 8 operations**, and there is no plain list-all endpoint:

| Method | Path | operationId | Notes |
|---|---|---|---|
| `POST` | `/v3/payments` | `create` | Create payment. Only `amount` (cents, int64, 1–999,999,999,999) is required. Optional: `reference` (≤35 chars), `bulkId` (≤35), `description` (≤140), `identifyCallbackUrl`, `callbackUrl`, `returnUrl` (all HTTPS URLs, ≤2048 chars, regex-validated), `voucherEligibility` (**deprecated**, don't implement). All three URLs fall back to profile-level defaults if omitted — **none are actually hard-required at the request level**, contrary to v1's assumption. `currency` is technically a field but the enum only has one value (`EUR`, defaulted) — could be omitted from the public API surface or kept for forward-compat; your call. |
| `GET` | `/v3/payments/{id}` | `merchant-get-payment` | Get by id — the polling fallback. |
| `DELETE` | `/v3/payments/{id}` | `cancel_payment` | Cancel — 204 on success. |
| `POST` | `/v3/payments/search` | `search` | **This is "list", not a GET.** Body: `{ from, to (date-time, from defaults to yesterday), paymentStatuses[] (enum array), reference }` — all optional filters. Paginated response (`AbstractListResponsePayment` + `details[]` of full payment objects). |
| `POST` | `/v3/payments/{id}/acknowledge` | `merchant-acknowledge` | Undocumented in v1 entirely. For the VOID-mode flow (`PENDING_MERCHANT_ACKNOWLEDGEMENT` status) — merchant confirms back. Body: `{ currency, amount, reference }`, both `currency`/`amount` required. |
| `GET` | `/v3/payments/{id}/debtor/refundIban` | `create-refund` *(misleading operationId — it's a GET)* | Returns the debtor's **unmasked** IBAN for issuing a refund. Response: `{ iban }`. This lives in the Payment spec, not the Refund spec, despite the name — needs the payment client, conceptually feeds the refund flow. |
| `POST` | `/v3/payments/pos` | `create_static_qr_payment` | Undocumented in v1. Creates a **static QR** payment (in-person, POS-style) — a different product line than the online checkout flow issue #3 was scoped around. Worth a scoping decision: in-scope for this wrapper, or explicitly out of scope since research-notes' original framing was "genuine online/e-commerce flow ... not just the in-person QR products"? |

Since the whole point of this library is to wrap what the API exposes rather than a
curated subset, issue #3 should be updated to cover all of these (or `/pos` explicitly
deferred with a documented reason) rather than just create/get/search/cancel.

## Corrected: the QR code URL is a distinct `_links.qrcode`, not `_links.self`

v1/issue #3 assumed `_links.self.href` doubles as the QR code URL. **The spec has both, as
separate link relations** on the `links` object:

- `self` — the payment resource URL (for polling — not a QR code)
- `qrcode` — the actual QR code image URL, e.g. `https://qrcodegenerator.api.bancontact.net/qrcode?c=...` (accepts `f=SVG|PNG`, `s=S|M|L|XL` per v1 — not restated in the schema itself, comes from the getting-started guide)
- `deeplink` — mobile deep link (`https://payconiq.com/pay/2/{id}`)
- `checkout` — hosted checkout page URL (present on create; example shows `?paymentId=...&timestamp=...&token=...` query params)
- `cancel` — only present while cancellable
- `refund` — only present once succeeded

Only `self`, `deeplink`, `qrcode` are marked `required` in the schema; `cancel`/`refund`/
`checkout` are conditionally present per the description ("depends on the status of the
payment").

## Spec inconsistency to flag, not silently work around

Both `get_payment_response` and `merchant-callback` schemas list `totalAmount` in their
`required` array, but neither schema actually **defines** a `totalAmount` property — only
`amount`. This looks like a leftover from a prior field rename that the `required` array wasn't
updated for. **Don't model a `TotalAmount` property that doesn't exist** — treat `required` as
having a spec bug here, and only bind to the properties that are actually defined
(`amount`, not `totalAmount`). Worth flagging to devsupport, but not blocking.

## Confirmed: error codes (literal, from spec response descriptions)

**Payment API** (`POST /v3/payments`):
`400 BODY_MISSING | FIELD_IS_REQUIRED | FIELD_IS_INVALID`, `401 UNAUTHORIZED`,
`403 ACCESS_DENIED`, `404 MERCHANT_PROFILE_NOT_FOUND`, `422 UNABLE_TO_PAY_CREDITOR`, `429`,
`500 TECHNICAL_ERROR`, `503 TRY_AGAIN_LATER`.

**Payment API** (`DELETE /v3/payments/{id}`):
`401 UNAUTHORIZED`, `403 ACCESS_DENIED | CALLER_NOT_ALLOWED_TO_CANCEL`,
`404 PAYMENT_NOT_FOUND`, `422 PAYMENT_NOT_PENDING`, `429`, `500 TECHNICAL_ERROR`.

**Payment API** (`GET /v3/payments/{id}`): `401 UNAUTHORIZED`, `403 ACCESS_DENIED`,
`404 PAYMENT_NOT_FOUND`, `429`, `500`, `503`.

**Refund API** (`POST /v3/payments/{payment-id}/refunds`):
`422 PAYMENT_FOR_REFUND_NOT_FOUND | INVALID_REFUND_AMOUNT | REFUND_NOT_ALLOWED | REFUND_NOT_POSSIBLE | REFUND_REQUEST_CONFLICT`
(`REFUND_REQUEST_CONFLICT` = the `Idempotency-Key` was reused with different parameters), plus
generic `400`, `401`, `403`, `500`, `503`.

**Reconciliation API**: `400 BAD_REQUEST`, `401 UNAUTHORIZED`, `403 ACCESS_DENIED`,
`404 PAYOUT_NOT_FOUND` (payments/refunds endpoints only), `500 TECHNICAL_ERROR`, `503`.

**Correction (2026-09-17, re-verified against the raw Refund/Reconciliation specs directly):**
the error shape is *not* identical across all three APIs as originally stated here. Only the
Payment API's `ErrorResponse` schema has `{ code, message, traceId, spanId }` (all four
required). The Refund and Reconciliation specs' own `ErrorResponse` schemas define only
`{ code, message }` — `traceId`/`spanId` aren't in those schemas at all, not just optional.
Model `TraceId`/`SpanId` as nullable and don't assume they'll be populated outside the Payment
API.

## Confirmed: Refund API schema (resolves v1's biggest unknown)

`POST /v3/payments/{payment-id}/refunds`, headers: `Idempotency-Key` (required, ≤64 chars) plus
the usual `Signature`.

Request body — **`amount` is required**, i.e. **partial refunds are supported by specifying any
amount**, not just a full-refund toggle:
```json
{ "amount": 1000, "currency": "EUR", "description": "optional, refund description" }
```
`amount`: int64 cents, 1–999,999,999,999 (same bounds as payment amount — no refund-specific cap
found in the spec; over-refund is presumably caught at runtime as `422 INVALID_REFUND_AMOUNT`).
No time-window field exists in the spec — if a window is enforced, it's a runtime business rule
surfaced via `REFUND_NOT_POSSIBLE`/`REFUND_NOT_ALLOWED`, not something to validate client-side.

Response (`RefundCreationResponse`, on `201`): `{ refundId, paymentId, status: "PENDING" (fixed one-value enum, matches the create-payment pattern), amount, currency, description?, creationDate }`.

`GET /v3/payments/{payment-id}/refunds/{refund-id}` response (`RefundModel`) is the same shape
but `status` is the **real** 3-value enum: `PENDING | REFUNDED | FAILED` (not `SUCCEEDED` —
v1 had this right, differs from the payment status enum's `SUCCEEDED`). `PROCESSING` and
`DEBTOR_IBAN_NOT_AVAILABLE` are indeed absent, confirming v1's changelog note.

## Confirmed: Reconciliation API schema

Matches v1 closely; a few exact field names confirmed from the raw spec:

- `GET /v3/reconciliation/payouts?date&size&page` → `{ size, totalPages, totalElements, number, payouts: [{ payoutId, merchantId, bulkId, iban, payoutStatus: SUCCEEDED|FAILED, payoutDate, payoutCurrency, totalPayments, totalRefunds, totalPaymentAmount, totalRefundAmount, payoutAmount }] }`
- `GET /v3/reconciliation/payments?payout-id|start-date&end-date&size&page` (max 30-day range) → `payments: [{ paymentId, paymentProfileId, merchantName, paymentChannel: ONLINE|INSTORE|INVOICE, currency, amount, reference?, description?, transactionDate }]`
- `GET /v3/reconciliation/refunds` — same as payments plus `refundId`.
- `size` default/max both 10000, `page` default 0 (zero-based) — matches v1.
- Same `Signature` header/JWS scheme as Payment/Refund (scheme name `JWS-Request-Signature` here, no suffix — same conflation as before, actual header is still `Signature`).

## Still open / not resolvable from the spec alone

- The `iss` claim discrepancy (Payment/Refund hardcode `"Payconiq"`, Reconciliation templates
  `"{Merchant Id}"`) — needs a direct question to devsupport, can't be resolved from docs alone.
- ~~Whether `/v3/payments/pos` (static QR / in-person) is in scope for this wrapper~~ —
  **resolved 2026-09-17: in scope.** The merchant's preprod onboarding request explicitly
  selected Static QR as one of its integration types, so this isn't hypothetical for this
  project, and the "wrap what the API exposes" scoping principle applies as normal.
- ~~No test-card/sandbox-simulation details surfaced in any of the fetched pages~~ — **resolved
  2026-09-17**, see the "Prose guides" section below. Test cards and app setup don't require
  preprod credentials to find; they were just on a page not fetched until now.
- **Discovered, not yet acted on (2026-09-17):** every 2xx/4xx response from the Reconciliation
  API declares its own `Signature` response header — Bancontact signs its own reconciliation
  responses, the same detached-JWS scheme used elsewhere. Neither the Payment nor Refund specs
  do this for ordinary (non-callback) responses. Not verified by `ReconciliationClient` (#7) —
  out of scope for that issue and not requested — but worth a follow-up issue if
  defense-in-depth response verification is ever wanted, since the machinery from #12/#13
  (JWKS client + `BancontactSignatureVerifier`) would mostly carry over.
- **Open (2026-09-17):** every prose guide page carries a banner announcing "New payment URLs"
  tied to "the decommissioning of the Payconiq brand in Belgium," rolling out to production
  **2026-11-05**. None of the pages checked (getting-started, callback guide,
  errors-and-statuses, online sales) actually state which hostnames/URLs are changing — the
  banner is generic across pages and the technical detail doesn't appear to be published yet, or
  lives somewhere not yet found. **Action before 2026-11-05**: check
  <https://docs.bancontactpro.com/> again for an updated banner with specifics, or ask
  devsupport directly — this could affect the hardcoded hostnames in
  `BancontactProServiceCollectionExtensions` (`merchant.api*.bancontact.net`,
  `jwks*.bancontact.net`) and the `payconiq.com`-namespaced JOSE header claim URIs, though the
  latter seem less likely to change since they're identifiers, not fetched URLs.

## Prose guides: real URLs confirmed, and what they add (2026-09-17)

Earlier passes referenced "the getting-started guide," "the callback guide," etc. without exact
URLs (summarized secondhand, not fetched directly). Confirmed real URLs by fetching
`https://docs.bancontactpro.com/` and its `.md` sibling and reading the actual navigation:

- [Getting Started](https://docs.bancontactpro.com/guides/general/gettingstarted052025v4)
- [Callback Guide](https://docs.bancontactpro.com/guides/general/callback052025)
- [Errors, statuses and responses](https://docs.bancontactpro.com/guides/general/errorsandstatuses052025)
- [On a Display](https://docs.bancontactpro.com/guides/instore/ondisplay052025v4)
- [On a Receipt](https://docs.bancontactpro.com/guides/instore/receipt052025v4)
- [Static QR](https://docs.bancontactpro.com/guides/instore/staticqr052025v4)
- [Top Up](https://docs.bancontactpro.com/guides/online/topup052025v4)
- [Invoice](https://docs.bancontactpro.com/guides/invoice/invoice052025v4)
- [Online Sales](https://docs.bancontactpro.com/guides/online/onlinesales) (see below —
  discontinued)
- [Refunds](https://docs.bancontactpro.com/guides/general/refunds052025)
- [Reconciliation](https://docs.bancontactpro.com/guides/general/reconciliation052025)
- [Payout & remittance](https://docs.bancontactpro.com/guides/general/payoutremittance052025)
- [Brand guidelines](https://brand.bancontactpro.com/) (separate site)

Each page is fetchable as raw markdown by appending `.md` to its URL — much more reliable to
parse than the rendered SPA (same lesson as [[research-verify-against-raw-spec]], now confirmed
to apply to the prose guides too, not just the OpenAPI JSON).

**Significant finding — Online Sales appears discontinued.** The Online Sales guide (the
hosted-checkout/redirect flow this project's README originally led with, before 2026-09-17)
carries this banner: *"Please note that this payment solution is no longer offered directly by
Bancontact Payconiq Company. If you would still like to use a similar solution in your business
please contact one of the partners listed here."* None of the in-store guides (On a Display, On
a Receipt, Static QR, Top Up) carry this notice. The merchant's actual preprod onboarding
request ([[project-merchant-entity-type]]) was for On a Display / On a Receipt / Static QR / Top
Up — **not** Online Sales — so this isn't a hypothetical concern for this project; the README
and getting-started.md have been reframed accordingly (2026-09-17) to lead with the in-store
products. The underlying `IPaymentClient` operations are unaffected — On a Display's
implementation guide confirms it uses the exact same `POST/GET/DELETE /v3/payments*` operations
as Online Sales, just under a different product/PPID configuration.

**Payment validity window is per-product, not a spec constant.** The OpenAPI spec doesn't state
an expiry at all. On a Display's guide states **2 minutes (120 seconds)**; Online Sales' guide
stated **20 minutes (1200 seconds)** — the "20 minutes" figure previously in this project's docs
came from the Online Sales guide specifically and has been corrected. Don't hardcode either
value; it's merchant-profile configuration, not something derivable from the API responses
themselves.

**Test cards and pre-production app setup found** (Getting Started guide) — previously flagged
in this document as unresolvable without credentials, but it turns out this was just on a page
not yet fetched:

| Issuer | Card Number | Expiry | Expected Result |
|---|---|---|---|
| KBC | `5127 8829 9999 9715` | Nov-29 | Always authorized |
| KBC | `5127 8829 9999 9723` | Nov-29 | Insufficient funds |
| KBC | `5127 8829 9999 9731` | Nov-29 | Card refused by issuer |

Pre-production Bancontact Pay app build: [download form](https://tally.so/r/nP8P1Q) (separate
from the production App Store/Play Store build — installing both causes redirection issues).
In-app onboarding uses fixed placeholder codes (`123456` for both email and phone verification,
no real OTP sent) — this is by design for the test environment, not a bug to route around.

**JWKS caching guidance confirmed** (Callback Guide) — matches this project's `JwksClient`
design (refresh-on-miss) without requiring a code change: Bancontact recommends caching JWKS for
up to **12 hours** and re-fetching on verification failure; a new JWK is added **24 hours**
before the old one is removed, giving a rotation overlap window. The callback guide's JOSE
header example also independently confirms the exact claim shape already documented above
(`crit` array, `https://payconiq.com/{sub,iss,iat,jti,path}`), and is the actual source of the
`PENDING_MERCHANT_AKNOWLEDGMENT` misspelling flagged earlier in this document — the errors-and-
statuses guide spells it that way in prose (`AKNOWLEDGMENT`), while the real OpenAPI enum is
`ACKNOWLEDGEMENT`. Confirmed the misspelling's exact source, not just its existence.

---

## v1 (original pass, kept for history — see corrections above before trusting anything here)

### What Bancontact Pro is

A rebrand of the former Payconiq platform. Product lineup centers on QR/bank-app payments
across four categories: On a Display, On a Receipt, Static QR, and Top Up (a closed-loop
card/wristband credit system). A genuine online/e-commerce flow exists separately (see below)
— not just the in-person QR products.

### Online payment flow

1. Merchant backend `POST`s to create a payment.
2. Customer is redirected to Bancontact's hosted checkout page, **or** the merchant renders
   its own checkout using the returned QR-code URL directly.
3. Customer confirms in their banking app.
4. Bancontact sends an asynchronous webhook to the merchant's `CallbackUrl` with status
   `PENDING` / `AUTHORIZED` / `FAILED`.
5. Customer is redirected back to the merchant's `ReturnUrl`.

**Callback/redirect ordering is not guaranteed** — the docs explicitly call this out, which is
why `GET /v3/payments/{id}` exists as a required fallback, not an optional nicety.

### Onboarding

Not self-serve. Production: apply via the merchant portal. Pre-production (sandbox): email
[devsupport@bancontact.com](mailto:devsupport@bancontact.com) with company name, Merchant ID,
and contact details — **up to two weeks** turnaround. No test-card/QR-simulation details were
found in the docs excerpts
gathered so far.

### Why build this as a separate library rather than inline in a consuming app

Mirrors the [`Eventbrite.Net`](https://github.com/DeWall88/Eventbrite.Net) pattern: a
general-purpose, non-app-specific API client (including the JWS signing/verification, which is
genuinely reusable logic) belongs in its own package, consumed via `PackageReference` by
whatever application needs it — not duplicated inline.

This library wraps the Bancontact Pro API as-is: it exposes what the API exposes, scoped to the
operations the raw OpenAPI specs define, not a curated subset or an opinionated redesign on top.

### Alternative considered

If *Bancontact the payment method* (rather than *Bancontact Pro the platform specifically*) is
ever the actual requirement, [Mollie](https://www.nuget.org/packages/Mollie.Api) has a mature
official .NET SDK with Bancontact support and a meaningfully simpler auth model (bearer key +
webhook secret, no JWS/JWKS). Noted here as a known trade-off, not a recommendation to switch.

**Not actually an option for this project**: the merchant here (Old Skool Lan) is a Belgian
*feitelijke vereniging* (unincorporated association). Mollie doesn't onboard unincorporated
associations; Bancontact Pro does. The simpler auth model above is moot — Mollie was never a
real alternative for this specific merchant, regardless of the JWS/JWKS complexity trade-off.
