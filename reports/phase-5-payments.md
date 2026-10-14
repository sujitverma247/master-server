# Phase 5 — Payments as gateway adapters

- Date: 2026-10-15
- Stack: ASP.NET Core .NET 9, EF Core + Npgsql, PostgreSQL 16, Docker Compose
- Verification: executed against the running Docker stack at http://127.0.0.1:8080

## Scope

Entities added: `Order` (publicId, idempotencyKey, customer, product, plan, amount `numeric(12,2)`, currency,
gateway, status, licenseId, refundPolicyId, external ids), `PaymentEvent` (unique gateway + externalEventId),
`GatewayPlan` (maps our product/plan to a provider catalog id). Migration: `Phase5Payments` (additive).

Adapter pattern (`IPaymentGateway`):

- `dev` — local signed webhook (`GATEWAY_DEV_SECRET`). Checkout URL is `/v1/checkout/{publicId}`.
- `manual` — no webhook; staff call mark-paid.
- Registry from env (`GATEWAY_DEFAULT`). Unknown gateway names 404/422. A future provider is a new adapter
  class plus `GatewayPlan` rows — provider catalog ids are never compiled into source.

Public endpoints:

- `POST /v1/checkout` — requires current refund-policy acceptance; price copied from the Plan row (client cannot set it)
- `GET /v1/checkout/{publicId}` — returns `licenseKey` only after paid
- `POST /v1/webhooks/{gateway}` — verify signature, insert `PaymentEvent`, fulfill (paid issues a key; refunded revokes)

Admin (`payment.read` / `payment.write`):

- `GET /admin/orders`, `GET /admin/orders/{id}`
- `POST /admin/orders/mark-paid`, `POST /admin/orders/refund`
- `GET/POST /admin/gateway-plans`

Fulfillment keeps the Phase 0 contract: issued keys have `ExpiresAt = null` until first desktop activation.

Decisions:

- Money is `decimal` rounded to 2 places (`Money.Normalize`).
- Client `idempotencyKey` (8–128 `[A-Za-z0-9._-]`) replays the same pending order; mismatch is 409.
- Duplicate webhook event id returns 200 `{ duplicate: true }` and does not re-issue a key.
- Webhook processing is transactional so a failed fulfill rolls back the event row.
- Audit stores `order#id` / masked key ends, never webhook secrets or full payloads (payload SHA-256 only).

## 1. Automated tests

`dotnet test`: **Total tests: 71, Passed 71, Failed 0.**

New in Phase 5 (19):

- `MoneyTests.Normalize_rounds_to_two_places` (4)
- `MoneyTests.Rejects_more_than_two_places`
- `MoneyTests.Non_negative`
- `IdempotencyKeyTests.Accepts_valid_keys` (2)
- `IdempotencyKeyTests.Rejects_invalid_keys` (4)
- `IdempotencyKeyTests.New_tokens_are_unique`
- `IdempotencyKeyTests.Sha256_is_stable`
- `DevGatewayTests.Sign_then_verify_roundtrips`
- `DevGatewayTests.Verify_rejects_wrong_secret`
- `DevGatewayTests.Verify_rejects_tampered_status`
- `DevGatewayTests.Registry_resolves_dev_and_manual`
- `DevGatewayTests.Manual_checkout_has_no_url`

Carried forward: 52. `PermissionsTests` now asserts agent has `payment.read` and lacks `payment.write`.

## 2. Container

`docker compose up -d --build` → image rebuilt, `master-db` healthy, `master-api` listening on `:8080`.
`GATEWAY_DEV_SECRET` passed from `.env` (not in source).

## 3. Live smoke (observed, 21/21 passed)

| # | Call | Expected | Result |
|---|---|---|---|
| 1 | `GET /health` | 200 | 200 |
| 2 | `POST /admin/login` | 200 | 200 |
| 3 | ensure `1year` plan | 201/exists | 201 |
| 4 | accept current policy | 200 | 200 |
| 5 | checkout without policy | 422 | 422 |
| 6 | `POST /v1/checkout` (dev) | 201 pending | 201 |
| 7 | same idempotency key | 200 same publicId | 200 |
| 8 | GET checkout while pending | no licenseKey | none |
| 9 | webhook bad signature | 401 | 401 |
| 10 | signed webhook `paid` | 200 paid | paid |
| 11 | same webhook again | duplicate=true | true |
| 12 | GET checkout after paid | licenseKey | `H34A4-JFUTQ-85U54-J9FLH` |
| 13 | desktop HMAC activate | 200 valid | 200 |
| 14 | `GET /admin/orders?status=paid` | ≥1 | 1 |
| 15 | `POST /admin/orders/refund` | 200 | 200 |
| 16 | activate after refund | 422 | 422 |
| 17 | checkout `manual` | 201 pending | 201 |
| 18 | admin mark-paid | paid + key | paid |
| 19 | `POST /admin/gateway-plans` | 201 | 201 |
| 20 | unknown key activate | 404 | 404 |
| 21 | bad HMAC | 401 | 401 |

## 4. Database verification (observed)

`__EFMigrationsHistory` lists 6 migrations ending in `20261014205605_Phase5Payments`.
New tables: `Orders`, `PaymentEvents`, `GatewayPlans`. Public table count is now 19 including history.

Orders: #1 `refunded`/`dev`/license 18; #2 `paid`/`manual`/license 19.
Events: `dev/paid` plus `manual/mark-paid`. Audit actions: `order.create`, `order.webhook`, `order.refund`,
`order.mark-paid`, `gateway-plan.create`.

## Known issues / notes

- Live provider adapters (hosted checkout HTTP calls) are not in this phase. The registry is the extension point;
  add an adapter + `GatewayPlan` rows when a provider is configured.
- `GATEWAY_DEV_SECRET` empty makes `dev` webhooks 401. Set it in deployment env.
- Smoke/verify scripts were run ad-hoc and removed; they are not committed.
- Local `.env` gained `GATEWAY_DEFAULT` / `GATEWAY_DEV_SECRET` (gitignored).

## Verdict

All mandatory Phase 5 gate checks executed and passed (71/71 unit tests, 21/21 live smoke incl. Phase 0 regressions,
clean additive migration, signed webhook idempotency, policy gate, key issue on paid). Phase 5 is complete.
Phase 6 (mail) not started.
