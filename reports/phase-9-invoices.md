# Phase 9 — Invoices, evidence, and PDF

- Date: 2026-10-15
- Stack: ASP.NET Core .NET 9, EF Core + Npgsql, PostgreSQL 16, Docker Compose
- Verification: executed against the running Docker stack at http://127.0.0.1:8080

## Scope

A paid order now issues exactly one `Invoice` (unique `OrderId`). The row is a snapshot: buyer email/name,
product/brand names, plan, amount (`numeric(12,2)`), gateway, order public id, and the refund-policy version
accepted at checkout. Migration: `Phase9Invoices` (additive).

Invoice numbers are `INV-YYYYMMDD-NNNN` (UTC day + daily sequence). Public access is email +
`publicId` or `orderPublicId` (no enumeration). PDF is generated with a small PDF 1.4 writer (Helvetica,
no native libraries, Alpine-safe). Bytes and SHA-256 are stored at issue time so the hash is stable
evidence. Refund (or admin void) sets status `void` and keeps the original PDF.

No QuestPDF dependency. No new mail templates. No desktop client changes. Brand names come from the
catalog snapshot, never from source.

Public:

- `GET /v1/invoices?email=&publicId=` or `orderPublicId=`
- `GET /v1/invoices/pdf?email=&publicId=`
- `GET /v1/evidence?email=&orderPublicId=` — invoice hash, policy acceptance, payment-event hashes.
  Public evidence omits `licenseId`. Never includes a license key or raw webhook payload.

Admin (`invoice.read` / `invoice.write`):

- `GET /admin/invoices`, `GET /admin/invoices/{id}`, `GET /admin/invoices/{id}/pdf`
- `POST /admin/invoices/void`
- `GET /admin/orders/{id}/evidence` (includes `licenseId`)

Agent is granted `invoice.read` only.

Decisions:

- Fulfillment issues the invoice inside the paid transaction; a second paid event is idempotent.
- Wrong email + real publicId is 404.
- PDF labels are brand-neutral (`Invoice`, `Bill to`, `Amount`, `Questions`).

## 1. Automated tests

`dotnet test`: **Total tests: 93, Passed 93, Failed 0.**

New (`InvoiceNumberTests` 4, `InvoicePdfTests` 4): format/parse, reject zero sequence, `%PDF-1.` header,
byte-stable SHA-256, brand-neutral labels, escaped parentheses.

Carried forward: 85. Agent grants now include `invoice.read` and deny `invoice.write`.

## 2. Container

`docker compose up -d --build` → rebuilt, listening on `:8080`. No extra Alpine packages.

## 3. Live smoke (observed, 18/18 passed)

| # | Call | Expected | Result |
|---|---|---|---|
| 1 | health | 200 | 200 |
| 2 | admin login | 200 | 200 |
| 3 | admin products | 200 | 200 |
| 4 | admin plans | 200 | 200 |
| 5 | current refund policy | 200 | 200 |
| 6 | accept policy | 200 | 200 |
| 7 | checkout (dev) | 201 | 201 |
| 8 | signed webhook paid | 200 paid | paid |
| 9 | admin invoices | issued + number + sha256 | `INV-20261014-0001` |
| 10 | public invoice | 200 | 200 |
| 11 | public PDF | `%PDF-1.4` | 1062 bytes `%PDF-1.4` |
| 12 | admin PDF | `%PDF-1.4` | 1062 bytes |
| 13 | public evidence | invoice + policy + payments, no licenseId | 200 |
| 14 | admin evidence | 200 | 200 |
| 15 | refund | 200 | 200 |
| 16 | invoice after refund | void | void |
| 17 | invoices, no token | 401 | 401 |
| 18 | public get wrong email | 404 | 404 |

## 4. Database verification (observed)

10 migrations, newest `20261014223200_Phase9Invoices`. After smoke: 1 invoice row, status `void`,
PDF stored as `bytea`.

## Known issues / notes

- Invoice day is UTC (`INV-20261014-…` when the local clock is already 2026-10-15 IST).
- No `invoice.issued` mail this phase; buyers still get `license.issued`.
- Smoke/verify scripts were run ad-hoc and removed.

## Verdict

All mandatory Phase 9 gate checks executed and passed (93/93 unit tests, 18/18 live smoke, additive
migration, paid → invoice → PDF → evidence → refund voids). Phase 9 is complete. Phase 10 (React admin,
separate repo) not started.
