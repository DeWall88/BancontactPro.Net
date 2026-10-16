# Architecture

How BancontactPro.Net is put together internally, and why. For a task-oriented walkthrough, see
[getting-started.md](getting-started.md). For the raw API research this was built from —
including spec quirks and open questions — see [research-notes.md](research-notes.md).

Everything here is derived from Bancontact's own raw OpenAPI specs, linked throughout below and
collected here for reference:
[Payment](https://docs.bancontactpro.com/_bundle/apis/merchant-payment.openapi.json) ·
[Refund](https://docs.bancontactpro.com/_bundle/apis/refund-public.openapi.json) ·
[Reconciliation](https://docs.bancontactpro.com/_bundle/apis/merchant-reconciliation.openapi.json) ·
[developer portal](https://docs.bancontactpro.com/).

## Project layout

```text
src/BancontactPro.Net/
├── Signing/        Outbound request signing + inbound signature verification (shared machinery)
├── Webhooks/        The callback DTO + the "verify and parse" entry point built on Signing/
├── Payments/        IPaymentClient / PaymentClient
├── Refunds/         IRefundClient / RefundClient
├── Reconciliation/  IReconciliationClient / ReconciliationClient
├── Models/          DTOs shared across the three typed clients and the webhook payload
├── Http/            BancontactApiException + shared error-response handling
├── QrCodes/         PaymentLinkBuilder -- client-templated QR/deeplink URLs (no HTTP call)
├── Helpers/         DI registration, the credentials provider, options + validator, endpoints, client factory
└── BancontactJsonOptions.cs   The one JsonSerializerOptions every DTO in this library uses
```

Every namespace maps 1:1 to a folder (`BancontactPro.Signing`, `BancontactPro.Webhooks`, etc.),
with a bare `BancontactPro` root namespace reserved for cross-cutting pieces like
`BancontactJsonOptions`.

## The signing scheme

Every outgoing request and every inbound webhook callback carries a **detached JWS** (JSON Web
Signature) using ES256 (ECDSA on P-256), computed per [RFC 7797](https://www.rfc-editor.org/rfc/rfc7797).
The scheme itself is documented in the `securitySchemes` section of each spec (e.g. the
`api_key_payment_profile`/JWS security scheme descriptions in the
[Payment API spec](https://docs.bancontactpro.com/_bundle/apis/merchant-payment.openapi.json)) —
this section summarizes what's there, plus a couple of things the prose descriptions get wrong or
leave ambiguous (see the quirks section below). This is *not* the usual 3-part compact JWS
(`header.payload.signature`) — Bancontact's variant has no payload segment at all:

```text
Signature header value = base64url(JOSE header) + "." + base64url(ES256 signature)
signature is computed over: base64url(JOSE header) + "." + base64url(body)
```

The JOSE header carries five custom claims under a `https://payconiq.com/` namespace, all
listed in the standard JWS `crit` (critical extensions) array:

```json
{
  "typ": "jose+json",
  "kid": "<the merchant's or Bancontact's key id>",
  "alg": "ES256",
  "https://payconiq.com/sub": "<merchant profile id>",
  "https://payconiq.com/iss": "Payconiq",
  "https://payconiq.com/iat": "2026-01-02T03:04:05.678Z",
  "https://payconiq.com/jti": "<unique id for this message>",
  "https://payconiq.com/path": "<the request path>",
  "crit": ["https://payconiq.com/sub", "https://payconiq.com/iss", "https://payconiq.com/iat", "https://payconiq.com/jti", "https://payconiq.com/path"]
}
```

This shape is genuinely bidirectional: the merchant signs outgoing requests with its own key,
and Bancontact signs inbound callbacks with its own (different) key — same header shape, same
`Signature` HTTP header name, opposite direction, different keys.

### Outbound: signing requests (`Signing/DetachedJwsSigner.cs`, `Signing/BancontactSigningHandler.cs`)

`DetachedJwsSigner.Sign(JoseHeader, body, ECDsa privateKey)` is the pure crypto/encoding core —
no HTTP, fully unit-testable. `BancontactSigningHandler : DelegatingHandler` wraps it into the
`HttpClient` pipeline: for every outgoing request it buffers the body, builds a fresh JOSE
header (new `iat`/`jti` per request, `path` from the actual request URI), signs it, and sets
both the `Signature` and `Authorization: Bearer {ApiKey}` headers before letting the request
continue down the pipeline. The client factory (`Helpers/IBancontactClientFactory.cs`) puts it in front of the Refund and
Reconciliation clients it creates, built per call from the current credentials (see "DI wiring"), so
the typed clients themselves never deal with signing directly — they just issue ordinary
relative-path HTTP calls.

The Payment client is **not signed**. Every Payment operation lists
`security: [{api_key_payment_profile}, {JWS-Request-Signature-Payment}]`, and in OpenAPI
separate entries in that list are alternatives, so the API key alone authenticates them.
`Http/BancontactApiKeyHandler.cs` sets only `Authorization: Bearer {ApiKey}`. Signing those calls
as well would add nothing, and with a `kid` Bancontact doesn't know it risks rejected requests.
Refund and Reconciliation list only the signature scheme, so they must be signed. That is why
the signing key is optional configuration (see docs/research-notes.md, "Corrected: which calls
need a signature").

The merchant's private key comes from `IRequestSigningKeyProvider`
(`Signing/EcPemRequestSigningKeyProvider.cs` is the default PEM-based implementation). The
inverse operation — exporting the corresponding public key as a JWK for Bancontact to host —
is `Signing/JwkExporter.cs`.

### Inbound: verifying callbacks (`Signing/JwksClient.cs`, `Signing/BancontactSignatureVerifier.cs`, `Webhooks/BancontactCallbackVerifier.cs`)

Three layers, each independently testable:

1. **`JwksClient`** fetches and caches Bancontact's JWKS (its published public keys). On a
   `kid` cache miss, it refreshes the whole cache once (to pick up key rotation) before
   concluding the key genuinely doesn't exist — it doesn't refresh on every call. Concurrent
   misses are coalesced into a single in-flight fetch via a `TaskCompletionSource`, not a
   `SemaphoreSlim`+self-clearing-field approach — that combination has a real race (see the
   commit history around 2026-09-17: a self-clearing in-flight marker can lose to its own field
   assignment when the underlying fetch completes synchronously, which reliably reproduces under
   parallel test execution even though it's rare in production). This refresh-on-miss strategy
   lines up with Bancontact's own
   [callback guide](https://docs.bancontactpro.com/guides/general/callback052025): a new JWK is
   added 24h before an old one is removed, and Bancontact recommends caching JWKS for up to 12h
   and re-fetching on verification failure — refreshing reactively on a miss (rather than on a
   fixed timer) satisfies that without needing a background refresh loop. The endpoint and the cache
   follow the provider's current environment, one cache per environment (see "The JWKS client follows
   the environment" below), and the instance is long-lived: never build one per call.

2. **`BancontactSignatureVerifier`** does the actual cryptographic and structural verification:
   splits the two segments, parses the JOSE header, rejects any `crit` entry it doesn't
   recognize (a deliberate JWS security requirement — an unrecognized critical extension must
   cause rejection, not silent ignoring), checks the header's `path` claim against the request's
   *actual* path (defends against a validly-signed message being replayed against the wrong
   route), resolves the signing key via `IJwksClient`, and verifies the signature. It returns the
   parsed claims (as a `JoseHeader`) on success, `null` on any failure — deliberately not an
   exception, since malformed/malicious inbound data is an expected, non-exceptional occurrence
   for anything facing the public internet.

3. **`BancontactCallbackVerifier`** is the actual entry point applications call. It wraps step 2
   and, only on success, deserializes the body into a `MerchantCallback`. On any failure
   (signature or deserialization), it throws `InvalidCallbackException` — unlike step 2, this is
   the application-facing API, and "this callback can't be trusted" is exactly the kind of thing
   that should stop request processing via an exception, not a value the caller might
   accidentally ignore.

## QR and deeplink URL templating (On a Receipt, Top Up, Static QR)

Every operation covered so far is an authenticated HTTP call. `QrCodes/PaymentLinkBuilder.cs` is
different — it makes no HTTP call at all. For On a Receipt and Top Up (and Static QR's
one-time-printed code), the QR/deeplink content isn't returned by any endpoint; the merchant
builds it locally by templating a known URL scheme, confirmed against Bancontact's
[Payment link update](https://docs.bancontactpro.com/guides/general/payloadurlupdate) migration
page and the per-product guides:

```text
payload (On a Receipt / Top Up)  = {pay host}/t/1/{PPID}?D={description}&A={amountCents}&R={reference}
payload (Static QR, printed once) = {pay host}/l/1/{PPID}/{posId}
qr image                          = {qr generator host}/qrcode?f={PNG|SVG}&s={S|M|L|XL}&c={url-encoded payload}
```

The hosts depend on the environment, so every method takes a `BancontactEnvironment` (callers pass
`BancontactCredentials.Environment` from the provider, or `BancontactProOptions.Environment`).
The builder stays a static class rather than a DI service:
it needs nothing else from configuration, and a required parameter can't be forgotten the way an
optional one defaulting to production could.

| Environment | `{pay host}` | `{qr generator host}` |
|---|---|---|
| `Production` | `https://pay.bancontact.net` | `https://qrcodegenerator.api.bancontact.net` |
| `Preprod` | `https://pay.acc.bancontact.net` | `https://qrcodegenerator.api.preprod.bancontact.net` |

The guides publish only the production hosts. The preprod pair comes from the links the preprod
API itself returns; [research-notes.md](research-notes.md) has the evidence and what is still
unverified. The production generator rejects the preprod pay host, which is why the payload and
the image URL must be built for the same environment.

Two encoding passes matter here, and `PaymentLinkBuilder` replicates both rather than
flattening them into one: the individual `D`/`A`/`R` values are UTF-8 percent-encoded once when
building the payload URL, then the *entire* payload URL is percent-encoded again when embedded
as the `c=` parameter. Getting this wrong (e.g. only encoding once) produces a QR the payer's
banking app can't parse. The test suite asserts exact output against the worked examples
Bancontact publishes on those pages, rather than just checking "it round-trips" — those are the
only ground truth available for an endpoint-less, pure-string-templating scheme like this one.

This gap wasn't visible from the OpenAPI specs at all (it's not an HTTP operation, so there was
nothing for the "verify against the raw spec" pass behind issues #2–#7 to find) — it only
surfaced from reading the On a Receipt/Top Up/Static QR prose guides directly. See
[research-notes.md](research-notes.md) for how it was found and the Static QR "location link vs.
per-transaction amount" distinction it depends on.

## Error handling

`Http/HttpResponseMessageExtensions.EnsureBancontactSuccessAsync()` is the one place that turns
a non-success HTTP response into an exception, shared by all three typed clients. It parses the
body as `Models/ErrorResponse.cs` and throws `BancontactApiException`.

**Important asymmetry, confirmed directly from the raw specs (not assumed):** the
[Payment API](https://docs.bancontactpro.com/_bundle/apis/merchant-payment.openapi.json)'s
`error` schema includes `traceId`/`spanId` (useful for support tickets); the
[Refund](https://docs.bancontactpro.com/_bundle/apis/refund-public.openapi.json) and
[Reconciliation](https://docs.bancontactpro.com/_bundle/apis/merchant-reconciliation.openapi.json)
APIs' `ErrorResponse` schemas define *only* `code`/`message` — those two fields aren't in the
schema at all outside the Payment API. `ErrorResponse.TraceId`/`SpanId` and
`BancontactApiException.TraceId`/`SpanId` are therefore nullable, and will genuinely be `null`
for Refund/Reconciliation errors — that's not a parsing failure, it's the correct shape.

If the response body isn't valid JSON at all (an upstream 502 from infrastructure, say, rather
than the API itself), `EnsureBancontactSuccessAsync()` falls back to a generic
`BancontactApiException` carrying the raw status code and whatever text was available, rather
than letting an unrelated `JsonException` obscure the real HTTP failure.

`RefundClient.CreateAsync` layers one more thing on top: a `REFUND_REQUEST_CONFLICT` response
(the `Idempotency-Key` was reused with different parameters) is caught and rethrown as
`RefundIdempotencyConflictException`, since that specific error is a caller bug rather than a
transient failure — code that blindly retries on `BancontactApiException` shouldn't retry that
one the same way.

## DI wiring (`Helpers/BancontactProServiceCollectionExtensions.cs`)

Two public registration methods share one core, so an application picks how its credentials reach
the library without changing anything else:

- `AddBancontactProIntegration(IServiceCollection, IConfiguration)` for static settings. It binds
  `BancontactProOptions` from the `BancontactPro` section, validates it on startup, and then calls
  `AddBancontactPro<ConfigurationCredentialsProvider>(ServiceLifetime.Singleton)`.
- `AddBancontactPro<TProvider>()` and `AddBancontactPro(Func<IServiceProvider, IBancontactCredentialsProvider>)`
  for a host that supplies its own `IBancontactCredentialsProvider`. They register no options and do no
  validation, and resolve nothing at startup, so a host with nothing configured still starts.

The core registers, as singletons: `IBancontactClientFactory`, `IJwksClient`,
`BancontactSignatureVerifier` and `BancontactCallbackVerifier`; plus two named `HttpClient`
pipelines (`BancontactPro.Api` for the three API clients, `BancontactPro.Jwks` for the JWKS fetch).

### Credentials are read per call, never captured

`IBancontactCredentialsProvider.GetCredentialsAsync` returns a `BancontactCredentials` snapshot:
the environment, the API key and PPID per product, the optional signing key and the `iss` claim.
The factory and the JWKS client call it on every use. They get it through `CredentialsAccessor`,
which opens a DI scope for the call, so the singletons can use a provider of any lifetime (a scoped
one that depends on a DbContext, for example). The library caches nothing about credentials, so a
provider that is expensive to read should cache briefly itself.

Because `IHttpClientFactory` caches a named client's handler pipeline (two minutes by default) and
fixes its `BaseAddress`, nothing credential-bearing may live there. So:

- The `BancontactPro.Api` pipeline holds only the primary handler (plus whatever the host adds
  with `ConfigureHttpClientDefaults`, such as resilience handlers).
- On each call to `PaymentsAsync` / `RefundsAsync` / `ReconciliationAsync`, the factory reads the
  snapshot, then builds a small `HttpClient` around that pipeline (`IHttpMessageHandlerFactory.CreateHandler`)
  with its own `BancontactApiKeyHandler` (Payment) or `BancontactSigningHandler` (Refund,
  Reconciliation), the `BaseAddress` from `BancontactEndpoints.GetMerchantApiBaseUri(environment)`, the
  timeout and the user agent. Those are the same handlers as before, so the headers, the signing and
  the JSON are unchanged; only where their inputs come from moved.
- The returned client keeps the credentials it was created with. That is why callers ask for one per
  operation rather than holding it.
- Parsing a PEM costs more than a request needs, so the factory keeps the last parsed signing key,
  keyed on its `kid` and PEM. A changed key misses the cache and replaces it.

The factory checks the snapshot before creating a client: `BancontactProductNotConfiguredException`
for a product without a complete pair, `BancontactSigningKeyNotConfiguredException` for a signed
client without a key, and `InvalidOperationException` for a key that isn't a valid PEM. All three
happen when the client is requested, so an unconfigured host fails only when the integration is used.
Bancontact issues credentials per product type, which is why a client is always bound to one product
rather than resolved directly from DI. The factory methods return `Task` because a provider may
read a database; `ConfigurationCredentialsProvider` returns completed tasks.

(The alternative, a synchronous factory with a handler that asks the provider inside `SendAsync`, was
rejected: missing-credential errors would surface in the middle of a call instead of at client
creation, `IsConfigured` could not be synchronous either, and the handler would have to rewrite the
request URI to follow an environment switch.)

### The JWKS client follows the environment

`BancontactEnvironment` (`Preprod` / `Production`, defaulting to `Preprod`) decides both the merchant
API host and the JWKS host; `BancontactEndpoints` is the one place that maps it to addresses.
`JwksClient` asks the provider for the environment on every `GetKeyAsync`, fetches from
`BancontactEndpoints.GetJwksUri(environment)`, and keeps one key cache and one in-flight refresh per
environment. Switching the environment at runtime therefore needs no restart, and one environment's
keys are never used for the other. The cache lives in the instance, so the registrations keep a single
long-lived `JwksClient` (the typed-client default would have been transient, and every consumer would
have had its own cache). Each fetch takes a fresh `HttpClient` from `BancontactPro.Jwks`, so the
pooled handlers still rotate; a consumer must not construct a `JwksClient` per call.

## Notable spec quirks encountered (and how they're handled)

These are things that would produce subtly wrong behavior if assumed rather than verified
against the raw OpenAPI specs — see [research-notes.md](research-notes.md) for the full list
and how each was discovered.

- **`totalAmount`** ([Payment API spec](https://docs.bancontactpro.com/_bundle/apis/merchant-payment.openapi.json),
  schemas `get_payment_response` and `merchant-callback`): both list `totalAmount` as `required`,
  but neither schema actually *defines* that property anywhere — a spec bug from an apparent
  field rename. `Payment` and `MerchantCallback` deliberately have no `TotalAmount` property;
  bind only to `Amount`.
- **`ExpiresAt` vs. `ExpireAt`** (same spec, schemas `payment_create_response` vs.
  `get_payment_response`): `CreatePaymentResponse.ExpiresAt` and `Payment.ExpireAt` are
  genuinely spelled differently in the spec (not the same field renamed) — both are modeled
  as-is rather than normalized to match each other.
- **The payment validity window is per-product configuration, not a spec constant**: the
  OpenAPI spec doesn't state a fixed expiry at all — it's set by which product the merchant
  profile is configured for. The
  [On a Display guide](https://docs.bancontactpro.com/guides/instore/ondisplay052025v4) states
  2 minutes; the
  [Online Sales guide](https://docs.bancontactpro.com/guides/online/onlinesales) (a hosted
  checkout/redirect product Bancontact currently states is "no longer offered directly") states
  20 minutes. This library doesn't hardcode either value anywhere — see
  [getting-started.md](getting-started.md#6-create-a-payment).
- **The `iss` claim discrepancy**: the
  [Payment](https://docs.bancontactpro.com/_bundle/apis/merchant-payment.openapi.json) and
  [Refund](https://docs.bancontactpro.com/_bundle/apis/refund-public.openapi.json) specs
  hardcode the signing `iss` claim to the literal string `"Payconiq"`; the
  [Reconciliation spec](https://docs.bancontactpro.com/_bundle/apis/merchant-reconciliation.openapi.json)'s
  scheme description instead templates it as `"{Merchant Id}"`. This is exposed as an
  overridable `Issuer` property on `BancontactProOptions`/`BancontactSigningOptions` (defaulting
  to `"Payconiq"`) rather than guessed at — it genuinely can't be resolved from documentation
  alone.
- **Reconciliation's `size` parameter isn't a range**
  ([Reconciliation API spec](https://docs.bancontactpro.com/_bundle/apis/merchant-reconciliation.openapi.json),
  `components.parameters.size`): the spec sets `size`'s minimum, maximum, *and* default all to
  `10000` — it's fixed, not a normal 0–10000 page-size choice like the Payment API's search
  endpoint. `IReconciliationClient` still exposes it as an optional parameter (in case that ever
  changes) but documents the constraint on the method itself.
- **Reconciliation responses carry their own `Signature` header** (same spec, every 2xx/4xx
  response declares a `Signature` entry under `headers`) — Bancontact signs its own
  reconciliation API responses, something neither the Payment nor Refund specs do for ordinary
  (non-callback) responses. Not currently verified by `ReconciliationClient` (out of scope for
  the issue that added it); the machinery in `BancontactSignatureVerifier`/`IJwksClient` would
  mostly carry over if response verification is ever wanted for defense-in-depth.

## Testing approach

Every layer is unit-tested against a fake `HttpMessageHandler` (for the typed clients and the
signing handler) or an in-memory fake `IJwksClient`/key pair (for the crypto layer) — no real
network calls, and none of the tests depend on real Bancontact credentials. The one thing that
*can't* be verified this way is round-tripping against Bancontact's actual pre-production
environment; see the root README's status note for where that stands.
