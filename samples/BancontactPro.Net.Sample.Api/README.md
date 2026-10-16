# BancontactPro.Net sample API

A small, standalone ASP.NET Core minimal API over the `BancontactPro.Net` library. It exists to:

- exercise **every library call** against Bancontact's preprod (or production) environment over plain HTTP, e.g. from Postman,
- give Bancontact a **public HTTPS callback URL** that verifies signatures with `BancontactCallbackVerifier`,
- serve as a runnable example of wiring the library into an app.

It does not depend on any other project.

It registers the library with `AddBancontactProIntegration(configuration)` because its settings are static (environment variables, validated when the app starts). An application whose credentials change at runtime, or that must start with none, registers an `IBancontactCredentialsProvider` with `AddBancontactPro<TProvider>()` instead: see [step 5 of the getting started guide](../../docs/getting-started.md#5-register-the-integration). Either way the routes are the same, and the QR routes read the environment and PPID from the provider.

## Routes

The sample is a **pass-through**. Every route is Bancontact's own path with the product in front, and calls exactly one library method, which calls exactly one Bancontact endpoint with that product's credentials:

```
POST /OnDisplay/v3/payments  →  (await clients.PaymentsAsync(OnDisplay)).CreateAsync  →  POST https://merchant.api…/v3/payments  (On Display's API key)
```

Request and response bodies are Bancontact's own shapes (see the [Merchant Payment API](https://docs.bancontactpro.com/_bundle/apis/merchant-payment.openapi.json)). The sample adds no logic about which product uses which call: that is in the [getting started guide](../../docs/getting-started.md#which-product-uses-which-call).

| Route | Library call | Bancontact endpoint |
|---|---|---|
| `POST /{product}/v3/payments` | `(await PaymentsAsync(product)).CreateAsync` | `POST /v3/payments` |
| `POST /{product}/v3/payments/pos` | `(await PaymentsAsync(product)).CreateStaticQrPaymentAsync` | `POST /v3/payments/pos` |
| `GET /{product}/v3/payments/{id}` | `(await PaymentsAsync(product)).GetAsync` | `GET /v3/payments/{id}` |
| `DELETE /{product}/v3/payments/{id}` | `(await PaymentsAsync(product)).CancelAsync` | `DELETE /v3/payments/{id}` |
| `POST /{product}/v3/payments/search?page&size` | `(await PaymentsAsync(product)).SearchAsync` | `POST /v3/payments/search` |
| `POST /{product}/v3/payments/{id}/acknowledge` | `(await PaymentsAsync(product)).AcknowledgeAsync` | `POST /v3/payments/{id}/acknowledge` |
| `GET /{product}/v3/payments/{id}/debtor/refundIban` | `(await PaymentsAsync(product)).GetDebtorRefundIbanAsync` | `GET /v3/payments/{id}/debtor/refundIban` |
| `POST /{product}/v3/payments/{id}/refunds` (`Idempotency-Key` header) | `(await RefundsAsync(product)).CreateAsync` | `POST /v3/payments/{id}/refunds` (signed) |
| `GET /{product}/v3/payments/{id}/refunds/{refundId}` | `(await RefundsAsync(product)).GetAsync` | `GET …/refunds/{refundId}` (signed) |
| `GET /{product}/v3/reconciliation/payouts?date&page&size` | `(await ReconciliationAsync(product)).GetPayoutsAsync` | `GET /v3/reconciliation/payouts` (signed) |
| `GET /{product}/v3/reconciliation/payments?payoutId&startDate&endDate&page&size` | `(await ReconciliationAsync(product)).GetPaymentsAsync` | `GET /v3/reconciliation/payments` (signed) |
| `GET /{product}/v3/reconciliation/refunds?payoutId&startDate&endDate&page&size` | `(await ReconciliationAsync(product)).GetRefundsAsync` | `GET /v3/reconciliation/refunds` (signed) |
| `GET /{product}/qr/payment-url?amount&description&reference&format&size` | `PaymentLinkBuilder.BuildFixedAmountPaymentUrl` + `BuildQrCodeImageUrl` | none: built with the product's PPID, on the configured environment's hosts |
| `GET /{product}/qr/pos/{posId}?format&size` | `PaymentLinkBuilder.BuildStaticQrLocationUrl` + `BuildQrCodeImageUrl` | none: the till's Static QR sticker, on the configured environment's hosts |
| `POST /bancontact/callback` | `BancontactCallbackVerifier` | Bancontact calls this |
| `GET /callbacks?paymentId` | none | The last 100 verified callbacks, newest first |
| `GET /health` | none | Liveness |

`{product}` is one of `OnDisplay`, `OnReceipt`, `StaticQr`, `TopUp`, `Invoice` (case-insensitive).

Everything except `/health` and the callback needs the `X-Sample-Key` header: the sample calls Bancontact on your behalf and is reachable from the internet.

Two things the sample does on top of the pass-through:

- **Default callback URL.** A create call without `callbackUrl` gets `PUBLIC_BASE_URL` + `/bancontact/callback`, so its callbacks show up in `GET /callbacks`. Send your own `callbackUrl` to override it.
- **Readable errors.** Failures come back as problem details instead of an empty 500: Bancontact's own status with its error `code`, message, `traceId` and `spanId` (what Bancontact support asks for), 400 naming the missing settings for a product without credentials or a signed call without a signing key, 404 for an unknown product, 502 when Bancontact can't be reached.

The callback URL Bancontact must call is `PUBLIC_BASE_URL` + `/bancontact/callback`. The signature covers the request path, so the proxy in front must not rewrite it.

**Postman:** import [`BancontactPro.Net.Sample.Api.postman_collection.json`](BancontactPro.Net.Sample.Api.postman_collection.json): one request per route, with test assertions. Set the collection variables `baseUrl`, `sampleKey` (in *Current value* only, so it isn't saved into exports) and `product`. The create requests remember the new payment's and refund's ID for the requests after them.

## Where Bancontact's credentials go

Bancontact's onboarding email gives **one API key and one PPID per product**. Each pair goes into that product's two variables:

| In the email, under a product | Compose variable | Library setting |
|---|---|---|
| "A unique **API key**" | `BANCONTACT_<PRODUCT>_API_KEY` | `BancontactPro:Products:<Product>:ApiKey` |
| "A unique **Product Profile Identifier** or PPID" | `BANCONTACT_<PRODUCT>_PPID` | `BancontactPro:Products:<Product>:MerchantProfileId` |
| "… and/or **JWS key**" | none | none: nothing to fill in, see below |

| Product in the email | `<PRODUCT>` in the variables | `<Product>` in the setting and the `product` field |
|---|---|---|
| Static QR | `STATICQR` | `StaticQr` |
| On Display | `ONDISPLAY` | `OnDisplay` |
| On a Receipt | `ONRECEIPT` | `OnReceipt` |
| Top Up | `TOPUP` | `TopUp` |
| Invoice | `INVOICE` | `Invoice` |

So for the Static QR entry in the email: its API key goes in `BANCONTACT_STATICQR_API_KEY` and its PPID in `BANCONTACT_STATICQR_PPID`. Leave the variables of products you don't have empty.

**A pair is all or nothing.** A product with only one of its two values set stops the app at startup with a message naming the missing setting. A product with neither is simply unavailable: any route for it returns 400 and nothing is sent to Bancontact. At least one product must be configured.

**About the "JWS key".** Bancontact's specs don't have them issue a private key to you. Callbacks are signed by Bancontact and verified against their public JWKS, which the library fetches by itself, so they need nothing from you. Only the Refund and Reconciliation APIs need a signature from *you*. For that you generate your own key pair, host the public half as a JWKS, and share its URL with Bancontact. Then fill in:

| Your own key | Compose variable | Library setting |
|---|---|---|
| A key id you choose | `BANCONTACT_KID` | `BancontactPro:Kid` |
| The EC P-256 private key (PEM) you generated | `BANCONTACT_PRIVATE_KEY_PEM` | `BancontactPro:PrivateKeyPem` |

The signing key is shared by all products. Leave both empty to test payments. If Bancontact does send you something labelled "JWS key", ask devsupport what it is before putting it anywhere.

## Other settings

| Setting (compose variable) | Meaning |
|---|---|
| `PublicBaseUrl` (`PUBLIC_BASE_URL`) | The public hostname, e.g. `https://bancontact-preprod.example.com`, no trailing slash. |
| `SampleApiKey` (`SAMPLE_API_KEY`) | Shared key (`X-Sample-Key` header) for every route except `/health` and the callback, a long random string you make up. Unset means those routes refuse everything. |
| `BancontactPro:Environment` (`BANCONTACT_ENVIRONMENT`) | `Preprod` (default) or `Production`. |

Credentials never go in the repository: use user-secrets locally, and a `.env` file or your container manager's environment settings on the host.

## Run locally

```bash
cd samples/BancontactPro.Net.Sample.Api
dotnet user-secrets init
dotnet user-secrets set "SampleApiKey" "<random>"
dotnet user-secrets set "BancontactPro:Products:StaticQr:ApiKey" "<Static QR's API key from the email>"
dotnet user-secrets set "BancontactPro:Products:StaticQr:MerchantProfileId" "<Static QR's PPID from the email>"
dotnet run
```

## The Docker image

GitHub Actions ([`sample-api-image.yml`](../../.github/workflows/sample-api-image.yml)) runs the tests and then publishes the image to GitHub Container Registry on every push to `main` that touches the library or the sample, and on demand:

- `ghcr.io/dewall88/bancontactpro-sample-api:latest`: what the compose file pulls by default.
- `ghcr.io/dewall88/bancontactpro-sample-api:sha-<short commit>`: one per build, to pin or roll back with `SAMPLE_API_IMAGE`.

The host never builds the image. Besides being faster, that keeps the stack deployable from container managers that can't run BuildKit builds. Portainer, for example, fails on a compose file with a `build:` section with "listing workers for Build".

For a public repository the package is public too, so any host can pull it without logging in. If yours is private (a private fork, for example), make the package public on GitHub (package page → **Package settings** → **Change visibility**) or give your host a registry login for `ghcr.io`.

To run a local build instead:

```bash
docker build -f samples/BancontactPro.Net.Sample.Api/Dockerfile -t bancontactpro-sample-api:local .
SAMPLE_API_IMAGE=bancontactpro-sample-api:local docker compose -f samples/BancontactPro.Net.Sample.Api/docker-compose.yml up -d
```

## Deploy

Any Docker host works, as long as Bancontact can reach the app over **public HTTPS**.

1. Run the stack with the variables from `.env.example`. Use either:
   - `docker compose -f samples/BancontactPro.Net.Sample.Api/docker-compose.yml up -d` on the host, or
   - a container manager pointed at this repository (reference `refs/heads/main`, compose path `samples/BancontactPro.Net.Sample.Api/docker-compose.yml`).
2. Put HTTPS in front of `http://<docker host>:<SAMPLE_API_PORT>` (default 8085) with a reverse proxy (Caddy, Traefik, nginx) or a tunnel (Cloudflare Tunnel, ...). Forward the path unchanged: the callback signature covers it.
3. Check `https://<hostname>/health`.
4. Give Bancontact `https://<hostname>/bancontact/callback`.

To update after a new image is published, pull it again and recreate the container: `docker compose pull && docker compose up -d`, or your container manager's redeploy with re-pulling the image switched on (in Portainer: **Pull and redeploy** with **Re-pull image**).

Consider making only `/bancontact/callback` and `/health` public at the proxy or tunnel (block everything else), on top of the shared key.
