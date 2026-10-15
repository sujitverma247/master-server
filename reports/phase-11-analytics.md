# Phase 11 — Dashboard analytics overview

- Date: 2026-10-15
- Stack: ASP.NET Core .NET 9, EF Core + Npgsql, PostgreSQL 16, Docker Compose
- Verification: executed against the running Docker stack at http://127.0.0.1:8080

## Scope

No new tables. `GET /admin/analytics/overview?days=7` (clamp 1–90) aggregates existing rows for the
admin dashboard. System status is **not** included (removed from the panel).

Permission: `analytics.read` (admin and agent). Seeded on boot like other codes.

UTC windows: inclusive start of `(today - (days-1))`, exclusive end of tomorrow. Deltas compare
creates (or paid-at for orders) in the current window vs the previous window of the same length.

| Dashboard widget | Source |
|---|---|
| Total licenses | `Licenses` count; delta from `CreatedAt` |
| Paid orders | `Orders` status `paid`; delta from `PaidAt` (fallback `CreatedAt`) |
| Customers | `Customers` count; delta from `CreatedAt` |
| Open tickets | `Tickets` status `open`; delta from tickets created in window |
| Pending mails | `MailOutbox` status `pending`; delta from mail created in window |
| Orders chart | paid orders bucketed by UTC day |
| Activations chart | `Activations.ActivatedAt` by UTC day |
| Recent orders | last 5 orders + customer email |
| Recent tickets | last 5 tickets by `UpdatedAt` |

License keys and webhook payloads are not returned.

The React dashboard (`admin-panel`) now calls this endpoint and no longer uses placeholder numbers.

## 1. Automated tests

`dotnet test`: **Total tests: 96, Passed 96, Failed 0.**

New (`AnalyticsWindowTests`, 3): clamp 1–90, 7-day window inclusive of today, delta percent
(including previous=0). Agent grants include `analytics.read`.

`admin-panel` vitest: **19 passed** (format helpers for deltas / day labels / status).

## 2. Container

`docker compose up -d --build` → rebuilt, listening on `:8080`. No schema migration.

## 3. Live smoke (observed, 5/5 passed)

| # | Call | Expected | Result |
|---|---|---|---|
| 1 | health | 200 | 200 |
| 2 | admin login + `analytics.read` | 200 | 200 |
| 3 | `GET /admin/analytics/overview?days=7` | 200, 7 labels, kpi totals | licenses=23 paid=1 customers=6 tickets=1 mail=0 |
| 4 | overview, no token | 401 | 401 |
| 5 | `days=400` | clamped to 90 | 90 |

## Known issues / notes

- Totals are all-time; the “vs previous 7 days” line is new-in-window vs previous-window, not a snapshot of the total 7 days ago.
- Existing admin sessions keep working; `analytics.read` is granted on next API boot via role-grant sync.
- Smoke/verify scripts were run ad-hoc and removed.

## Verdict

All Phase 11 gate checks passed (96/96 server tests, 19/19 panel tests, 5/5 live smoke). Dashboard
widgets other than the removed System Status now have a real API. Not committed unless requested.
