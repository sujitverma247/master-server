# Phase 4 — Customers and refund-policy acceptance

- Date: 2026-10-15
- Stack: ASP.NET Core .NET 9, EF Core + Npgsql, PostgreSQL 16, Docker Compose
- Verification: executed against the running Docker stack at http://127.0.0.1:8080

## Scope

Entities added: `Customer` (unique normalized email, displayName, notes, source, externalCustomerId, isActive),
`RefundPolicy` (version, title, body, isCurrent, effectiveAt), `RefundPolicyAcceptance` (customer + policy, unique pair,
ip, user-agent, source). `License` gained nullable `CustomerId` (FK → `Customers`, `OnDelete SetNull`).
Migration: `Phase4Customers` (additive only).

Admin endpoints (permission-gated):

- `GET /admin/customers` (`customer.read`) — filter `q`, `limit`; includes `acceptedCurrent`
- `GET /admin/customers/{id}` (`customer.read`) — licenses + acceptance history
- `POST /admin/customers` (`customer.write`)
- `POST /admin/customers/update` (`customer.write`) — email change syncs `License.CustomerEmail`
- `GET /admin/refund-policies` (`customer.read`)
- `POST /admin/refund-policies` (`customer.write`) — optional `activate` (default true) demotes the previous current
- `POST /admin/refund-policies/{id}/activate` (`customer.write`)

Public checkout endpoints (no admin token; Phase 5 will call these before creating an order):

- `GET /v1/refund-policy/current`
- `GET /v1/refund-policy/status?email=`
- `POST /v1/refund-policy/accept` `{ email, policyVersion, displayName? }` — upserts customer, idempotent on current policy

Key issuance (`POST /panel/keys/issue` / key update) now upserts a `Customer` when `customerEmail` is present and sets
`License.CustomerId`. Invalid emails return 400.

`RefundPolicyGate.HasAcceptedCurrentAsync` is the hook Phase 5 payments will use. If no current policy exists, acceptance
is not required.

Decisions:

- Buyer identity is lowercase trimmed email (`EmailAddress`). Policy versions are `a-z0-9.-` (`PolicyVersion`).
- Accepting a non-current version is 422. Re-accepting the current version is 200 `alreadyAccepted:true`.
- Customers are not hard-deleted (deactivate via `isActive`). Acceptances restrict-delete so history stays.
- Default policy `2026-10-v1` is seeded when none exist. Body is brand-neutral.
- Audit stores emails / versions / customer ids, never secrets.

## 1. Automated tests

`dotnet test`: **Total tests: 52, Passed 52, Failed 0.**

New in Phase 4:

- `EmailAddressTests.Normalizes_trim_and_case` (2)
- `EmailAddressTests.Rejects_invalid` (8)
- `EmailAddressTests.Rejects_over_max_length`
- `PolicyVersionTests.Normalizes_version` (2)
- `PolicyVersionTests.Rejects_invalid_version` (5)
- `PolicyVersionTests.Seed_default_version_is_valid`

Carried forward: 33 (HmacSignature 2, PasswordHasher 4, SessionToken 2, Permissions 3, PlanCatalog 11, KeyGenerator 11).
`PermissionsTests` now also asserts agent has `customer.read` and lacks `customer.write`.

> One test initially failed (`Rejects_over_max_length`): the constructed address was 255 chars, just under the 256 cap.
> The test input was lengthened. Production validator unchanged.

## 2. Container

`docker compose up -d --build` → image rebuilt, `master-db` healthy, `master-api` running.
Log: `Seeded default refund policy 2026-10-v1.` then `Now listening on http://[::]:8080`.

## 3. Live smoke (observed, 22/22 passed)

Secrets loaded from `.env` at runtime.

| # | Call | Expected | Result |
|---|---|---|---|
| 1 | `GET /health` | 200 | 200 |
| 2 | `POST /admin/login` | 200 | 200 |
| 3 | `GET /v1/refund-policy/current` | 200 | 200 `2026-10-v1` |
| 4 | `GET /v1/refund-policy/status` (new email) | accepted=false | false |
| 5 | accept stale version | 422 | 422 |
| 6 | accept invalid email | 400 | 400 |
| 7 | accept current | 200, first time | customerId=1, alreadyAccepted=false |
| 8 | accept again | alreadyAccepted=true | true |
| 9 | status after accept | accepted=true | true |
| 10 | `GET /admin/customers?q=` | acceptedCurrent=true | true |
| 11 | `GET /admin/customers/{id}` | 200 | email matched |
| 12 | create duplicate email | 409 | 409 |
| 13 | admin create customer | 201 | 201 |
| 14 | `POST /panel/keys/issue` with email | customerId linked | customerId=1 |
| 15 | customer detail licenses | ≥1 | 1 |
| 16 | create+activate `2026-10-v2` | 201 | 201 |
| 17 | status after new current | accepted=false, v2 | false / `2026-10-v2` |
| 18 | list customers, no token | 401 | 401 |
| 19 | agent `GET /admin/customers` | 200 | 200 |
| 20 | agent `POST /admin/customers` | 403 | 403 |
| 21 | activate unknown key (HMAC) | 404 | 404 |
| 22 | activate bad HMAC | 401 | 401 |

## 4. Database verification (observed)

`__EFMigrationsHistory` lists exactly:

- `20261014180232_InitialCreate`
- `20261014195200_Phase1Identity`
- `20261014200649_Phase2Catalog`
- `20261014202741_Phase3Keys`
- `20261014204650_Phase4Customers`

Public tables (16 incl. history): previous 13 plus `Customers`, `RefundPolicies`, `RefundPolicyAcceptances`.
Policies: `2026-10-v1` not current, `2026-10-v2` current. Customers: `phase4-buyer@example.com`, `phase4-manual@example.com`.

Audit (emails/versions only):

```
policy.create   | 2026-10-v2
customer.create | phase4-manual@example.com
policy.accept   | phase4-buyer@example.com
```

## Known issues / notes

- Existing Phase 3 licenses that only have `CustomerEmail` are not backfilled onto `Customer` rows. New issues and updates
  create/link the customer. A one-off backfill is not in this phase.
- After a new policy becomes current, previous acceptances no longer satisfy `HasAcceptedCurrent`. Buyers must re-accept.
- Smoke/verify scripts were run ad-hoc and removed; they are not committed.

## Verdict

All mandatory Phase 4 gate checks executed and passed (52/52 unit tests, 22/22 live smoke incl. Phase 0/1 regressions,
clean additive migration, seeded policy, masked audit). Phase 4 is complete. Phase 5 (payments as gateway adapters) not started.
