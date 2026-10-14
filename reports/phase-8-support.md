# Phase 8 — Support tickets

- Date: 2026-10-15
- Stack: ASP.NET Core .NET 9, EF Core + Npgsql, PostgreSQL 16, Docker Compose
- Verification: executed against the running Docker stack at http://127.0.0.1:8080

## Scope

Entities added: `Ticket` (publicId, customer, optional productId/licenseId, subject, status, priority) and
`TicketMessage` (author customer|staff, authorName, body). Migration: `Phase8Support` (additive).
License keys are looked up and stored as `LicenseId` only — the full key is never written on the ticket.

Statuses: `open` → staff reply → `waiting` → customer reply → `open`. `closed` rejects further customer
or staff replies until reopen. Public tickets are identified by email + unguessable `publicId` (32 hex, Guid N).
Sequential ids are admin-only.

Public (no auth; email + publicId required together so tickets cannot be enumerated):

- `POST /v1/support/tickets` `{ email, subject, body, displayName?, productId?, licenseKey?, priority? }`
  upserts the customer (`source=support`)
- `GET /v1/support/tickets?email=&publicId=`
- `POST /v1/support/tickets/reply` `{ email, publicId, body }`

Admin (`support.read` / `support.write`):

- `GET /admin/tickets` — filter `status`, `customerId`, `q`
- `GET /admin/tickets/{id}`
- `POST /admin/tickets/reply` `{ id, body }`
- `POST /admin/tickets/close` `{ id }`
- `POST /admin/tickets/reopen` `{ id }`

Agent is granted **both** `support.read` and `support.write` (support is their job). No new mail templates
this phase. No desktop client changes.

Decisions:

- Wrong email + real publicId is 404 (same as unknown id).
- Subject max 200, body max 8000. Closed stays closed on any message until staff reopen.
- No brand names in the model or sample copy.

## 1. Automated tests

`dotnet test`: **Total tests: 85, Passed 85, Failed 0.**

New (`TicketRulesTests`, 6): status/priority sets, customer cannot reply when closed, customer message
reopens waiting, staff message moves open to waiting, subject/body caps, public id is 32 hex and not sequential.

Carried forward: 79. Agent grants now include `support.read` and `support.write`.

## 2. Container

`docker compose up -d --build` → rebuilt, listening on `:8080`.

## 3. Live smoke (observed, 16/16 passed)

| # | Call | Expected | Result |
|---|---|---|---|
| 1 | health | 200 | 200 |
| 2 | admin login | 200 | 200 |
| 3 | open ticket | 201 + publicId 32 hex + status open | 201 |
| 4 | public get email+publicId | 200 | 200 |
| 5 | admin list `?status=open` | ≥1 | 200 |
| 6 | admin get by id | 200 | 200 |
| 7 | staff reply | 200 + waiting | waiting |
| 8 | customer reply | 200 + open | open |
| 9 | close | 200 + closed | closed |
| 10 | public get closed | 200 | 200 |
| 11 | customer reply on closed | 422 | 422 |
| 12 | public get wrong email | 404 | 404 |
| 13 | reopen | 200 + open | open |
| 14 | admin list, no token | 401 | 401 |
| 15 | open, invalid email | 400 | 400 |
| 16 | open, unknown licenseKey | 422 | 422 |

## 4. Database verification (observed)

9 migrations, newest `20261014221410_Phase8Support`. After smoke: `Tickets` = 1 row, `TicketMessages` = 3 rows
(customer open + staff reply + customer reply).

## Known issues / notes

- No outbound mail on ticket open/reply this phase. Phase 6 templates stay as-is; a later pass can enqueue
  `ticket.opened` / `ticket.replied` if wanted.
- Smoke/verify scripts were run ad-hoc and removed.

## Verdict

All mandatory Phase 8 gate checks executed and passed (85/85 unit tests, 16/16 live smoke, additive
migration). Phase 8 is complete. Phase 9 (invoices/evidence/PDF) not started.
