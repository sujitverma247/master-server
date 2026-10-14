# Master Server

Generic license API. Product names, app keys, and HMAC secrets are configuration. They are not compiled into this repository.

## Stack

- ASP.NET Core on .NET 9
- EF Core and Npgsql
- PostgreSQL 16
- Docker Compose

## What this slice includes

- `GET /health` returns `{ "status": "ok" }`.
- Tables for products, license keys, device activations, and used nonces.
- `POST /v1/license/activate`
- `GET /v1/license/status?key=`
- `POST /v1/license/deactivate`
- HMAC headers: `User-Agent`, `X-App-Key`, `X-Timestamp`, `X-Nonce`, `X-Signature`.
- Signed string: `{appKey}:{timestamp}:{nonce}:{path}:{rawQuery}`. The body is not signed. The query string is the raw query, without decoding it first.
- Activate returns `valid`, `isActive`, `expiresAt`, `planType`, and `maxMachines`.
- Status returns `valid`, `isActive`, `isRevoked`, `expired`, `expiresAt`, and `planType`.
- An unknown key is 404. A bad signature, stale timestamp, or reused nonce is 401.
- Seat limits run inside a database transaction that locks the license row.

A desktop client must be mounted against the site root so the signed path is `/v1/license/...`.

## Run

Copy `.env.example` to `.env` and set the product values there. Then:

```bash
docker compose up --build
```

`GET http://127.0.0.1:8080/health` returns `{ "status": "ok" }`.

The API listens on localhost port 8080. Put TLS in front of it in production.

## Product configuration

Set these in the environment or in `.env`. Leave them empty and the API still starts, with no product loaded.

| Variable | Purpose |
|---|---|
| `PRODUCT_SLUG` | Stable product id. Defaults to `default`. |
| `PRODUCT_DISPLAY_NAME` | Display name. |
| `PRODUCT_APP_KEY` | Value the client sends in `X-App-Key`. |
| `PRODUCT_USER_AGENT_PREFIX` | Required prefix of the `User-Agent` header. |
| `PRODUCT_HMAC_SECRET` | HMAC secret for that product. Stored on the product row. |
| `PRODUCT_KEY_PREFIX` | Prefix reserved for later key generation. |
| `SEED_LICENSE_KEY` | Optional key inserted once, for a local check. |
| `SEED_PLAN_TYPE` | `monthly`, `1year`, `2year`, `3year`, `5year`, or `lifetime`. |
| `SEED_MAX_MACHINES` | Seat limit copied onto that seeded key. |

On startup the process applies EF migrations, then upserts the product when the app key, User-Agent prefix, and HMAC secret are all set. The secret is never written into source files and is not returned by these routes.

Point the desktop client's license server URL at this API. The three product values must match the values compiled into that client.

Plan codes `lifetime` stay non-expiring for a client that treats that `planType` as no expiry. Other plans set `expiresAt` on first activation. If `expiresAt` is already stored, first activation keeps it.

## Not in this slice

Brands, plan catalog, customers, purchases, subscriptions, payment gateways, refund-policy acceptance, email, admin login, permissions, audit, tickets, telemetry, invoices, and the admin UI. Those stay out until this license slice is running.
