# Phase 3 — Key lifecycle (generation, issuance, revocation, lookup)

- Date: 2026-10-15
- Stack: ASP.NET Core .NET 9, EF Core + Npgsql, PostgreSQL 16, Docker Compose
- Verification: executed against the running Docker stack at http://127.0.0.1:8080

## Scope

Entity added: `KeyBatch` (id, name, productId, planType, quantity, maxMachines, notes, generatedBy, generatedAt).
`License` extended with: `BatchId` (FK → `KeyBatches`, `OnDelete SetNull`), `Notes`, `Source`, `IsSubscription`,
`ExternalSubscriptionId`, `ExternalPaymentId`, `ExternalCustomerId`, `CustomerEmail`. Indexes added on
`Licenses.BatchId`, `Licenses.CustomerEmail`, `Licenses.ExternalSubscriptionId`, and `KeyBatches.ProductId`.
Migration: `Phase3Keys` (additive only — `AddColumn`/`CreateTable`/`CreateIndex`/`AddForeignKey`; no data loss).

Key format (`Keys/KeyGenerator.cs`): `PREFIX-XXXXX-XXXXX-XXXXX-XXXXX` where `PREFIX` is the product's `KeyPrefix`
(omitted when empty, yielding 4 groups). Alphabet `ABCDEFGHJKLMNPQRSTUVWXYZ23456789` — ambiguous `0/O/1/I` excluded.
Uniqueness: in-memory dedupe + DB existence check with top-up retry.

Admin/panel/agent endpoints (`Keys/KeysApi.cs`, permission-gated):

- `POST /admin/keys/generate` (`key.issue`) — generate a batch (cap 5000), returns keys + batch id.
- `POST /panel/keys/issue` (`key.issue`) — issue a single key; optional explicit key (409 on collision), customerEmail, source, notes.
- `GET /admin/keys` (`key.read`) — filter by `productId`, `batchId`, `status` (active/revoked/inactive/unused/activated), `q`, `limit`.
- `GET /admin/keys/{id}` (`key.read`) — detail incl. activations.
- `GET /admin/keys/export` (`key.read`) — CSV, optional `batchId`/`productId`.
- `POST /admin/keys/revoke` (`key.revoke`) — by id or key.
- `POST /admin/keys/revoke-batch` (`key.revoke`) — bulk via `ExecuteUpdateAsync`.
- `POST /admin/keys/update` (`key.revoke`) — plan/seats/notes/email/active/revoked.
- `POST /admin/keys/delete` (`key.revoke`) — hard delete.
- `GET /agent/lookup` (`key.read`) — by key/email/subscription, audited.

Decisions:

- Keys are created with `ExpiresAt = null` (not started). First desktop activation sets `ActivatedAt` and computes
  `ExpiresAt` from `PlanCatalog.DurationDaysAsync` — unchanged Phase 0 contract, so the shipped client needs no change.
- Audit never stores full keys: `Mask(key)` = `****` + last 4. Targets are `license#<id>` / `batch#<id>` / masked key / email.
- `maxMachines` defaults from the matching active `Plan` row (fallback 3), clamped 1–100.

## 1. Automated tests

`dotnet test`: **Total tests: 33, Passed 33, Failed 0.**

New in Phase 3 (`KeyGeneratorTests`, 11 cases):

- `Generated_key_has_prefix_and_four_groups`
- `No_prefix_yields_four_groups_only`
- `Alphabet_excludes_ambiguous_characters` (200 keys scanned for `0/O/1/I`)
- `Prefix_is_normalized` (7 cases incl. lowercase, embedded `-`, over-length clamp, all-ambiguous → empty, trim)
- `Keys_are_practically_unique` (1000 unique keys)
- `IsValidFormat_rejects_malformed`

Carried forward from Phases 1–2 (22): HmacSignature 2, PasswordHasher 4, SessionToken 2, Permissions 3, PlanCatalog 11.

> Note: one test initially failed (`Prefix_is_normalized` expected `TOOL` for `toolongprefix`). This was a wrong test
> expectation — `O` is not in the alphabet, so the correct normalization is `TLNG`. The test was corrected; the generator
> behaves as designed. No production code changed as a result.

## 2. Container

`docker compose up -d --build` → image rebuilt, `master-db` healthy, `master-api` running, `Now listening on http://[::]:8080`.

## 3. Live smoke (observed, 17/17 passed)

Secrets loaded from `.env` at runtime (not hardcoded in the test script).

| # | Call | Expected | Result |
|---|---|---|---|
| 1 | `GET /health` | 200 | 200 |
| 2 | `POST /admin/login` | 200 | 200, token |
| 3 | `GET /admin/products` | 200 | 200, productId=1 slug=`default` |
| 4 | `POST /admin/keys/generate` qty 5 | 201 | 201 |
| 5 | batch returns 5 keys | count=5 | 5 |
| 6 | key format | `PREFIX?-XXXXX×4` | `PYJRK-MJQTX-794M8-JF38U` (empty prefix ⇒ 4 groups) |
| 7 | `POST /panel/keys/issue` (customerEmail) | 201 | 201, `5K62Y-B45VA-PW44V-GXKR5` |
| 8 | `GET /admin/keys?batchId=` | 5 | 5 |
| 9 | `POST /v1/license/activate` issued key (HMAC) | valid:true | 200, `expiresAt=2027-10-14…` (1year) |
| 10 | `GET /agent/lookup?email=` | ≥1 | 2 results |
| 11 | `POST /admin/keys/revoke` (by key) | 200 | 200 |
| 12 | activate again after revoke | 422 | 422 `license revoked` |
| 13 | `POST /admin/keys/revoke-batch` | revoked=5 | 5 |
| 14 | `GET /admin/keys/export?batchId=` | CSV | 200, header `key,productId,…` |

## 4. RBAC + Phase 0 regression (observed)

| Call | Expected | Result |
|---|---|---|
| `POST /admin/keys/generate` without token | 401 | 401 `unauthorized` |
| activate unknown key (signed) | 404 | 404 `invalid license key` |
| activate with bad `X-Signature` | 401 | 401 `unauthorized` |

The activate/revoke/unknown-key/bad-HMAC results confirm Phase 0 behavior is intact after the schema change.

## 5. Database verification (observed)

`__EFMigrationsHistory` lists exactly:

- `20261014180232_InitialCreate`
- `20261014195200_Phase1Identity`
- `20261014200649_Phase2Catalog`
- `20261014202741_Phase3Keys`

Public tables (13 incl. history): `Activations, AdminSessions, AdminUsers, AuditLogs, Brands, KeyBatches, Licenses,
Permissions, Plans, Products, RolePermissions, UsedNonces, __EFMigrationsHistory`. Migration applied cleanly.

Audit trail (masked, no full keys):

```
key.export       | batch#2
key.revoke-batch | batch#2
key.revoke       | license#16
key.lookup       | email=buyer@example.com
key.issue        | license#16
key.generate     | batch#2
```

## Known issues / notes

- The seeded `default` product has an empty `KeyPrefix`, so generated keys have 4 groups and no prefix — valid per the
  generator. Products created with a non-empty `KeyPrefix` produce `PREFIX-…` keys.
- A revoked, never-activated key still has `IsActive:true`; `valid` is the authoritative signal (Phase 2 note still applies).
- Local shell note: `docker compose exec` / nested PowerShell via the harness prints `Add-Content` stream warnings; these
  are display artifacts and do not affect results. `Invoke-WebRequest` requires `-UseBasicParsing` under non-interactive PS 5.1.
- Smoke/verify scripts were run ad-hoc and removed; they are not committed.

## Verdict

All mandatory Phase 3 gate checks executed and passed (33/33 unit tests, 17/17 live smoke incl. Phase 0 regression,
clean additive migration, masked audit). Phase 3 is complete. Phase 4 (customers + refund-policy acceptance) not started.
