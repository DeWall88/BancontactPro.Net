# BancontactPro.Net sample API

A small, standalone ASP.NET Core minimal API that uses the `BancontactPro.Net` library. It exists to:

- give Bancontact a **public HTTPS callback URL** that verifies signatures with `BancontactCallbackVerifier`,
- exercise the library against Bancontact's **preprod** environment end to end,
- serve as a runnable example of wiring the library into an app.

It does not depend on any other project. Payments are kept **in memory** (a restart forgets them); swap `IPaymentStore` for a database in a real application.

## Endpoints

| Method and path | Auth | What it does |
|---|---|---|
| `GET /health` | none | Liveness. |
| `POST /bancontact/callback` | Bancontact's JWS `Signature` header | Verifies the callback, then applies the status idempotently. Invalid signature: 401. Valid but duplicate, stale or unknown: 200. |
| `POST /payments` | `X-Sample-Key` | Creates a payment for one product. Body: `{ "product": "StaticQr", "amount": 1000, "reference": "...", "description": "...", "posId": "..." }` (amount in cents). With `posId` it creates a Static QR payment, without it a regular one. A product without its API key and PPID: 400, naming the missing settings. |
| `GET /payments/{id}[?refresh=true]` | `X-Sample-Key` | The stored payment. `refresh=true` asks Bancontact first, with the credentials of the product the payment was created for: the fallback for a late or lost callback. |
| `DELETE /payments/{id}` | `X-Sample-Key` | Cancels the payment, again with its own product's credentials. |

`product` is one of `OnDisplay`, `OnReceipt`, `StaticQr`, `TopUp`, `Invoice`.

The callback URL Bancontact must call is `PUBLIC_BASE_URL` + `/bancontact/callback`. The signature covers the request path, so the proxy in front must not rewrite it.

## Where Bancontact's credentials go

Bancontact's onboarding email gives **one API key and one PPID per product**. Each pair goes into that product's two variables:

| In the email, under a product | Compose variable (Portainer) | Library setting |
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

**A pair is all or nothing.** A product with only one of its two values set stops the app at startup with a message naming the missing setting. A product with neither is simply unavailable: creating a payment for it returns 400 and nothing is sent to Bancontact. At least one product must be configured.

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
| `SampleApiKey` (`SAMPLE_API_KEY`) | Shared key for `/payments`, a long random string you make up. Unset means those endpoints refuse everything. |
| `BancontactPro:Environment` (`BANCONTACT_ENVIRONMENT`) | `Preprod` (default) or `Production`. |

Credentials never go in the repository: use user-secrets locally and the stack's environment in Portainer.

## Run locally

```bash
cd samples/BancontactPro.Net.Sample.Api
dotnet user-secrets init
dotnet user-secrets set "SampleApiKey" "<random>"
dotnet user-secrets set "BancontactPro:Products:StaticQr:ApiKey" "<Static QR's API key from the email>"
dotnet user-secrets set "BancontactPro:Products:StaticQr:MerchantProfileId" "<Static QR's PPID from the email>"
dotnet run
```

## Deploy on the Docker host behind Cloudflare

1. Create the stack in Portainer from `docker-compose.yml` (Repository method, compose path `samples/BancontactPro.Net.Sample.Api/docker-compose.yml`), or run `docker compose -f samples/BancontactPro.Net.Sample.Api/docker-compose.yml up -d --build` on the host. Set the variables from `.env.example`.
2. Add a Cloudflare Tunnel public hostname that routes to `http://<docker host>:<SAMPLE_API_PORT>` (default 8085).
3. Check `https://<hostname>/health`.
4. Give Bancontact `https://<hostname>/bancontact/callback`.

Consider restricting the hostname in Cloudflare so only `/bancontact/callback` and `/health` are public (a WAF or Access rule on `/payments*`), on top of the shared key.
