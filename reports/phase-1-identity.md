# Phase 1 — Identity and RBAC

- Date: 2026-10-15
- Stack: ASP.NET Core .NET 9, EF Core + Npgsql, PostgreSQL 16, Docker Compose
- Source control: `master-server/` is untracked (gitignored by the parent product repo); no commit hash

## Scope

Endpoints added:

- `POST /admin/login`
- `POST /admin/logout`
- `GET /admin/me`
- `GET /admin/users`
- `POST /admin/users`
- `POST /admin/users/update`
- `GET /admin/audit`

Entities added: `AdminUser`, `AdminSession`, `Permission`, `RolePermission`, `AuditLog` (migration `Phase1Identity`).

Security decisions:

- Passwords hashed with PBKDF2-SHA256, 100k iterations, format `iterations.saltB64.hashB64`.
- Session tokens are random 256-bit values; only a SHA-256 hash is stored, so a database leak cannot replay a token.
- Permissions are granular codes, not just admin/agent. A role's effective permissions are the union of its `RolePermission` rows.
- Admin gets every code; agent is read-only (`audit.read`, `product.read`, `key.read`, `customer.read`, `payment.read`).
- Deactivating a user deletes its sessions; a password change clears its other sessions.
- CORS is restricted to `ADMIN_ORIGIN`; empty means no browser origin is allowed. Desktop and webhooks do not use CORS.
- The first admin is seeded only from `ADMIN_SEED_USERNAME` / `ADMIN_SEED_PASSWORD` and only when no admin users exist. No credentials in source.

## Automated tests

`dotnet test`: 11 passed, 0 failed.

- PasswordHasher: `Hash_then_verify_roundtrips`, `Verify_rejects_wrong_password`, `Hashes_are_salted_and_differ_each_time`, `Verify_rejects_malformed_stored_value`
- SessionToken: `New_tokens_are_unique_and_url_safe`, `Hash_is_stable_and_hides_the_token`
- Permissions: `Admin_role_is_granted_every_permission_in_the_catalog`, `Agent_role_is_read_only_and_cannot_manage_users_or_issue_keys`, `Every_granted_code_exists_in_the_catalog`
- HMAC (Phase 0, still passing): `Sign_covers_app_key_timestamp_nonce_path_and_raw_query`, `Activate_message_uses_an_empty_query`

## Live smoke (Docker, http://127.0.0.1:8080)

| Call | Expected | Result |
|---|---|---|
| `POST /admin/login` (admin) | 200 + token + permissions | 200 |
| `POST /admin/login` (wrong password) | 401 | 401 `invalid credentials` |
| `GET /admin/me` (admin) | 200, 11 permissions | 200 |
| `POST /admin/users` (create agent) | 201, 5 agent permissions | 201 |
| `POST /admin/login` (agent) | 200 | 200 |
| `GET /admin/users` (agent, needs `user.manage`) | 403 | 403 `forbidden` |
| `GET /admin/audit` (agent, has `audit.read`) | 200 | 200, entries present |
| `GET /admin/me` (no token) | 401 | 401 `unauthorized` |
| `GET /admin/users` (admin) | 200, 2 users | 200 |
| `POST /admin/users/update` deactivate agent | 200 | 200 |
| `GET /admin/me` with revoked agent token | 401 | 401 |
| `POST /admin/login` deactivated agent | 401 | 401 |
| `POST /admin/users/update` deactivate self | 422 | 422 `cannot deactivate yourself` |

Audit log recorded `auth.login`, `auth.login.failed`, `user.create`, and `user.update`. Targets are usernames; no secrets stored.

## Regression (Phase 0 desktop contract)

| Call | Result |
|---|---|
| `GET /health` | `{ "status": "ok" }` |
| `POST /v1/license/activate` TEST-KEY-0001 | `valid: true`, `planType: 1year`, `maxMachines: 3` |
| `GET /v1/license/status` | `valid: true`, `isRevoked: false`, `expired: false` |

## Not done yet

Catalog (brands/products/plans), key issuance, customers, payments, mail, telemetry, support, invoices. React admin panel is deferred to after Phase 9.

## Follow-ups

- Rotate the dev admin password (`ADMIN_SEED_PASSWORD`) before any real deployment; the seed is for local testing only.
- Consider login rate limiting / lockout in a later hardening pass.
