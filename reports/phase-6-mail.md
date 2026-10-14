# Phase 6 — Mail

- Date: 2026-10-15
- Stack: ASP.NET Core .NET 9, EF Core + Npgsql, PostgreSQL 16, Docker Compose
- Verification: executed against the running Docker stack at http://127.0.0.1:8080

## Scope

Entity added: `OutboundMail` / table `MailOutbox` (template, idempotency key, to/from, subject, body, status,
provider, attempts, lastError, product/customer/order/license ids). Migration: `Phase6Mail` (additive).

Adapters (`IMailSender`), same idea as payment gateways:

- `console` (default) — marks sent, no network. Used in local Docker.
- `smtp` — `System.Net.Mail` when `MAIL_PROVIDER=smtp` and `MAIL_SMTP_HOST` is set.

Templates are brand-neutral (`license.issued`, `order.refunded`). `{{productName}}`, `{{brandName}}`,
`{{supportEmail}}` come from the Product/Brand row or `MAIL_FROM` / `MAIL_FROM_NAME`. No product names
are compiled into source.

Triggers (enqueue + drain after the payment/key transaction commits, so a send failure cannot roll back a sale):

- Order paid → `license.issued` (idempotent per `order#{id}`)
- Order refunded → `order.refunded`
- Panel key issue with a customer email → `license.issued` (idempotent per `license#{id}`)

Admin (`mail.read` / `mail.write`):

- `GET /admin/mail` — list (no body)
- `GET /admin/mail/{id}` — detail including body (staff debug; buyer already has the key)
- `POST /admin/mail/resend`
- `POST /admin/mail/flush`

Decisions:

- Outbox first: every send is a row. Duplicate webhooks do not create a second license email.
- Audit stores `mail#{id}` + template + to-email, never the license key.
- From-address: Brand.SupportEmail if valid, else `MAIL_FROM`. Missing from → `failed` / `missing from address`.
- Agent may read mail, not resend.

## 1. Automated tests

`dotnet test`: **Total tests: 76, Passed 76, Failed 0.**

New in Phase 6 (`MailRendererTests`, 5):

- `License_issued_substitutes_vars`
- `Refund_includes_order_id`
- `Unknown_template_throws`
- `Catalog_templates_are_brand_neutral` (rejects deutsche / antivirus / musix / firewall in template source)
- `Known_template_ids`

Carried forward: 71. Permissions now grant agent `mail.read` and deny `mail.write`.

## 2. Container

`docker compose up -d --build` → rebuilt, listening on `:8080`.
`MAIL_PROVIDER=console`, `MAIL_FROM=support@localhost.local` from `.env` (not in source).

## 3. Live smoke (observed, 15/15 passed)

| # | Call | Expected | Result |
|---|---|---|---|
| 1 | health | 200 | 200 |
| 2 | admin login | 200 | 200 |
| 3 | 1year plan | exists | exists |
| 4 | accept policy | 200 | 200 |
| 5 | checkout | 201 | 201 |
| 6 | signed webhook paid | 200 | 200 |
| 7 | `GET /admin/mail?template=license.issued` | sent | id=1 sent |
| 8 | mail detail body contains key | yes | from=`support@localhost.local` |
| 9 | provider | console | console |
| 10 | resend | 200 | 200 |
| 11 | refund | 200 | 200 |
| 12 | `order.refunded` mail | sent | id=2 |
| 13 | panel key issue | 201 + mail | 201 (outbox id=3) |
| 14 | mail without token | 401 | 401 |
| 15 | unknown key activate | 404 | 404 |

## 4. Database verification (observed)

7 migrations, newest `20261014212456_Phase6Mail`.
`MailOutbox`: 3 rows, all `sent` / `console` (issued, refunded, manual issue).
Audit: `mail.resend` → `mail#1`.

## Known issues / notes

- SMTP is implemented but not live-tested (no local SMTP). Switch `MAIL_PROVIDER=smtp` and set `MAIL_SMTP_*` in deployment.
- HTML templates and attachments wait for Phase 9 invoices if needed.
- Smoke/verify scripts were run ad-hoc and removed.
- Local `.env` gained `MAIL_*` (gitignored).

## Verdict

All mandatory Phase 6 gate checks executed and passed (76/76 unit tests, 15/15 live smoke, clean additive
migration, brand-neutral templates, outbox idempotency). Phase 6 is complete. Phase 7 (telemetry / heartbeats)
not started.
