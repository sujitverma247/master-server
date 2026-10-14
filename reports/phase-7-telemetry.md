# Phase 7 — Telemetry (heartbeats and status-by-machine)

- Date: 2026-10-15
- Stack: ASP.NET Core .NET 9, EF Core + Npgsql, PostgreSQL 16, Docker Compose
- Verification: executed against the running Docker stack at http://127.0.0.1:8080

## Scope

`Activation` gained `LastSeenAt`. New `Heartbeat` table (licenseId, activationId, machineId, app/os/ip, seenAt).
Migration: `Phase7Telemetry` (additive). Heartbeats older than 14 days are deleted on write.

HMAC endpoints (same signing contract as activate/status/deactivate):

- `POST /v1/license/heartbeat` `{ key, machineId, appVersion?, osVersion? }`
  - 200 if the machine currently holds a seat
  - 422 if revoked / inactive / expired / not seated
  - 404 unknown key, 401 bad HMAC
  - Does **not** create a seat
- `GET /v1/license/status?key=` — **unchanged payload** when `machineId` is omitted (shipped client)
- `GET /v1/license/status?key=&machineId=` — same fields plus `machineSeated`, `lastSeenAt`

Admin (`telemetry.read`):

- `GET /admin/machines?machineId=&licenseId=&productId=&seated=1`
- `GET /admin/heartbeats?machineId=&licenseId=`

`online` = seated and last-seen within `HEARTBEAT_STALE_HOURS` (default 72). Last-seen falls back to `ActivatedAt`.

Activate now refreshes `LastSeenAt` on an existing seat. The shipped desktop client still does not send heartbeats;
the endpoint is ready for a later client update. No desktop code was changed.

Decisions:

- Extra JSON fields are only added when `machineId` is present so Phase 0 status stays the same.
- Audit on machine list stores the machine id, never a license key.
- Agent may read telemetry.

## 1. Automated tests

`dotnet test`: **Total tests: 79, Passed 79, Failed 0.**

New (`MachinePresenceTests`, 3): last-seen fallback, online inside window, boundary inclusive.

Carried forward: 76. Agent grants now include `telemetry.read`.

## 2. Container

`docker compose up -d --build` → rebuilt, listening on `:8080`.

## 3. Live smoke (observed, 15/15 passed)

| # | Call | Expected | Result |
|---|---|---|---|
| 1 | health | 200 | 200 |
| 2 | admin login | 200 | 200 |
| 3 | issue key | 201 | 201 |
| 4 | HMAC activate | 200 | 200 |
| 5 | status `?key=` only | valid, no `machineSeated` | valid |
| 6 | status `?key=&machineId=` | machineSeated=true | true |
| 7 | heartbeat | 200 + lastSeenAt | 200 |
| 8 | heartbeat other machine | 422 | 422 |
| 9 | admin machines | seated + online | true |
| 10 | admin heartbeats | ≥1 | 1 |
| 11 | deactivate | 200 | 200 |
| 12 | heartbeat after deactivate | 422 | 422 |
| 13 | unknown key heartbeat | 404 | 404 |
| 14 | bad HMAC | 401 | 401 |
| 15 | machines, no token | 401 | 401 |

## 4. Database verification (observed)

8 migrations, newest `20261014220326_Phase7Telemetry`. `Heartbeats` table present (1 row from smoke).

## Known issues / notes

- The current desktop client does not call `/v1/license/heartbeat`. Periodic re-check is still `GET /v1/license/status?key=`.
  Adding heartbeat to the client is a later product change, not this phase.
- Smoke/verify scripts were run ad-hoc and removed.

## Verdict

All mandatory Phase 7 gate checks executed and passed (79/79 unit tests, 15/15 live smoke, Phase 0 status
payload preserved, additive migration). Phase 7 is complete. Phase 8 (support/tickets) not started.
