# Phase 2 — Catalog (brands, products, plans)

- Date: 2026-10-15
- Stack: ASP.NET Core .NET 9, EF Core + Npgsql, PostgreSQL 16, Docker Compose
- Verification: executed against the running Docker stack at http://127.0.0.1:8080

## Scope

Entities added: `Brand`, `Plan`; `Product` extended with `BrandId`, `LatestVersion`, `DownloadUrl`, `ReleaseNotes` (migration `Phase2Catalog`).

Admin endpoints (permission-gated):

- `GET /admin/brands` (`product.read`), `POST /admin/brands` (`product.write`), `PUT /admin/brands/{id}` (`product.write`)
- `GET /admin/products` (`product.read`), `POST /admin/products` (`product.write`), `PUT /admin/products/{id}` (`product.write`), `POST /admin/products/{id}/deactivate` (`product.write`)
- `GET /admin/plans` (`product.read`), `POST /admin/plans` (`plan.write`), `PUT /admin/plans/{id}` (`plan.write`)

Public endpoint (HMAC): `GET /v1/app/update-check?version=` returns `latestVersion`, `downloadUrl`, `releaseNotes`, `updateAvailable` for the authenticated product.

Decisions:

- Plan durations come from a `Plan` row when one exists for the license's `PlanType`; otherwise the built-in defaults apply (`PlanCatalog`).
- Product `HmacSecret` is write-only: settable via create/update, never returned. Lists show `hasHmacSecret` only. `AppKey` is intentionally visible (it is a public client header, not a secret).
- `price` stored as `numeric(12,2)`; `currency` upper-cased; plan `code` lower-cased. Slugs: 2-64 chars of `a-z`, `0-9`, `-`.

## 1. Automated tests (reconciled)

`dotnet test`: **Total tests: 22, Passed 22, Failed 0.** The Phase 2 report count (22) matches the observed run.

Observed test names:

- `HmacSignatureTests.Sign_covers_app_key_timestamp_nonce_path_and_raw_query`
- `HmacSignatureTests.Activate_message_uses_an_empty_query`
- `PasswordHasherTests.Hash_then_verify_roundtrips`
- `PasswordHasherTests.Verify_rejects_wrong_password`
- `PasswordHasherTests.Hashes_are_salted_and_differ_each_time`
- `PasswordHasherTests.Verify_rejects_malformed_stored_value`
- `SessionTokenTests.New_tokens_are_unique_and_url_safe`
- `SessionTokenTests.Hash_is_stable_and_hides_the_token`
- `PermissionsTests.Admin_role_is_granted_every_permission_in_the_catalog`
- `PermissionsTests.Agent_role_is_read_only_and_cannot_manage_users_or_issue_keys`
- `PermissionsTests.Every_granted_code_exists_in_the_catalog`
- `PlanCatalogTests.Default_duration_matches_plan_code` (10 cases: monthly, 1year, 2year, 3year, 5year, lifetime, LIFETIME, " 1year ", unknown, "")
- `PlanCatalogTests.LicenseApi_delegates_to_the_default_table`

Breakdown: HmacSignature 2, PasswordHasher 4, SessionToken 2, Permissions 3, PlanCatalog 11 (10 theory + 1 fact) = 22.

## 2. Container

`docker compose up -d --build` → image built, `master-db` healthy, `master-api` running.

## 7. Migrations and secret-in-log check

`__EFMigrationsHistory` lists exactly:

- `20261014180232_InitialCreate`
- `20261014195200_Phase1Identity`
- `20261014200649_Phase2Catalog`

Public tables present: `Brands, Products, Plans, Licenses, Activations, UsedNonces, AdminUsers, AdminSessions, Permissions, RolePermissions, AuditLogs` (+ `__EFMigrationsHistory`). Migrations apply cleanly.

API log scan: default HMAC secret present = **False**, acme HMAC secret present = **False**, admin password present = **False**. No secret values in logs.

## 3. Catalog endpoint results (observed)

| Call | Expected | HTTP | Note |
|---|---|---|---|
| `POST /admin/login` | 200 | 200 | token + 11 admin permissions |
| `GET /admin/brands` | 200 | 200 | brand `acme` |
| `GET /admin/products` | 200 | 200 | 2 products, `hasHmacSecret` only, no secret value |
| `GET /admin/plans` | 200 | 200 | plan `2year`, price 149.00 EUR |
| `POST /admin/brands` dup slug `acme` | 409 | 409 | `slug already exists` |
| `POST /admin/plans` dup code `2year` | 409 | 409 | `code already exists` |
| `POST /admin/brands` slug `"A B"` | 400 | 400 | slug validation message |
| `POST /admin/products` throwaway | 201 | 201 | secret not echoed |
| `POST /admin/products/{id}/deactivate` | 200 | 200 | `isActive:false` |

## 4. Cross-product auth, invalid signatures, signed queries (observed)

| Call | Expected | HTTP |
|---|---|---|
| product-1 app key signed with product-2 secret | 401 | 401 `unauthorized` |
| product-2 app key signed with product-1 secret | 401 | 401 `unauthorized` |
| product-1 correct secret, wrong User-Agent prefix | 401 | 401 `unauthorized` |
| tampered `X-Signature` | 401 | 401 `unauthorized` |
| `GET /v1/license/status?key=…` signed over the raw query | 200 | 200 `valid:true` |
| same status call signed over an empty query (mismatch) | 401 | 401 `unauthorized` |

The last two together prove the raw query string is part of the signed message. Cross-product app key/secret pairs cannot authenticate.

## 5. Phase 0 regression (observed)

| Call | Expected | HTTP | Note |
|---|---|---|---|
| `GET /health` | 200 | 200 | `{ "status": "ok" }` |
| activate `SEAT-TEST-0001` machine A (max 1) | valid | 200 | `maxMachines:1` |
| activate `SEAT-TEST-0001` machine B | seat limit | 422 | `machine limit reached` |
| re-activate machine A (same machine) | valid, no new seat | 200 | same `expiresAt` |
| `GET status SEAT-TEST-0001` | valid | 200 | `valid:true` |
| deactivate machine A | ok | 200 | `deactivated:true` |
| activate machine B after seat freed | valid | 200 | seat reused |
| activate unknown key | 404 | 404 | `invalid license key` |
| activate `REVOKED-0001` | 4xx (not 401) | 422 | `license revoked` |
| activate with invalid HMAC | 401 | 401 | `unauthorized` |
| `GET status REVOKED-0001` | 200, flags | 200 | `valid:false, isRevoked:true` |

## 6. Phase 1 regression (observed)

| Call | Expected | HTTP | Note |
|---|---|---|---|
| agent `GET /admin/users` | 403 | 403 | `forbidden` (needs `user.manage`) |
| agent `GET /admin/audit` | 200 | 200 | entries returned (`audit.read`) |
| admin `POST /admin/brands` | 201 | 201 | authorized write |
| admin `GET /admin/audit` | 200 | 200 | shows `brand.create`, `user.create`, `product.deactivate` |

Audit targets are slugs/usernames; no secrets stored.

## Known issues / notes

- The second product's license and the seat/revoked test licenses were inserted via SQL because key issuance arrives in Phase 3. After Phase 3 these use `POST /admin/keys/generate`.
- CORS for `ADMIN_ORIGIN` is configured but was not exercised with a browser origin in this run (no admin origin set); it is covered functionally when the React app lands (after Phase 9).
- A revoked, never-activated license reports `isActive:true` with `isRevoked:true` and `valid:false`; `isActive` is the license enable flag and is independent of revoke. `valid:false` is the authoritative signal and is correct.
- `AppKey` is returned by `/admin/products` by design (public client header). The HMAC secret is never returned.
- Local shell note: piping into `docker compose exec` via the test harness prints `Add-Content` stream warnings; these are harness display artifacts and do not affect SQL execution or results.

## Verdict

All mandatory Phase 2 gate checks executed and passed. No failures. Phase 2 is complete. Phase 3 (key issuance) not started.
