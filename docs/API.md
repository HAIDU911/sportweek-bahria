# API reference (v1)

Base URL `http://localhost:5000`. Auth: `Authorization: Bearer <Supabase access token>`. Errors always use
`{ "code", "message", "fieldErrors", "correlationId" }`. Every response carries `X-Correlation-Id`.
Interactive docs: `/swagger` (Development only).

| Status | Meaning |
|---|---|
| 400 | validation (see `fieldErrors`) |
| 401 | missing/invalid/expired token |
| 403 | wrong role, unverified account, or domain not permitted |
| 404 | not found |
| 409 | conflict (duplicate team/player, team limit, stale version) |
| 422 | business rule / domain invariant |
| 429 | rate limited (`Retry-After`) |
| 503 | dependency (database) unavailable |

## Health
- `GET /health` — liveness, never touches the database.
- `GET /health/ready` — readiness; `503` (no details) when the database is unreachable.

## Public (no login, 120 req/min/IP, cacheable 15 s, safe projections only)
- `GET /api/public/sports`
- `GET /api/public/tournaments?status=&page=&pageSize=`
- `GET /api/public/tournaments/{id}`
- `GET /api/public/tournaments/{id}/fixtures?page=&pageSize=`
- `GET /api/public/teams?search=&page=&pageSize=` (approved teams only)
- `GET /api/public/leaderboards/overall`
- `GET /api/public/home` — one call for the home page: stats, live matches, today's fixtures, latest results, next up, top-5 standings, sports. Cached 5 s (the UI auto-refreshes every 5 s).
- `GET /api/public/fixtures?status=&sportId=&tournamentId=&day=&page=&pageSize=` — fixture feed with department, live period and clock.
- `GET /api/public/tournaments/{id}/standings` — approved teams with played/wins/losses.

## Identity
- `GET /api/me` — the caller's profile and **server-assigned** role. `403 identity.rejected` explains domain/verification failures.

## Captain (role Captain, verified email)
- `GET /api/captain/teams` — only the caller's own teams (scope comes from the token, never a parameter).
- `POST /api/captain/tournaments/{id}/teams` — header `Idempotency-Key: <8–64 chars>` required.
  Body `{ teamName, department, players: [{ fullName, enrollmentNo, department }] }` → `201 { teamId }`.
  Same key + same caller returns the same team (safe retry).

- `GET /api/captain/notifications` · `POST /api/captain/notifications/read-all`
- `GET /api/captain/fixtures` — fixtures of the caller's own teams only.
- `POST /api/captain/teams/{id}/withdraw` `{ reason }` — own team only; opponents in unplayed matches advance by walkover.

## Operator desk (role **Scorer** only — the read-only Admin is rejected with 403)
All mutations validate role → state → input → optimistic `version` → persist → notify captains → audit. A stale `version` returns `409 version_conflict`.

| Endpoint | Business rule |
|---|---|
| `GET /api/ops/tournaments` · `POST /api/ops/tournaments` | overview incl. drafts · create (unique per sport) |
| `POST /api/ops/tournaments/{id}/status` `{status}` | Draft→Open→Closed (reopen only before fixtures exist) →InProgress→Completed; Cancelled from any non-final state |
| `GET /api/ops/teams?status=&search=` · `GET /api/ops/teams/{id}` | review queue; captain email and enrollments masked |
| `POST /api/ops/teams/{id}/review` `{decision: approve\|reject, reason, version}` | Pending only; reject needs 5–300 char reason; locked once fixtures exist; captain notified |
| `POST /api/ops/tournaments/{id}/fixtures/generate` | needs Closed registration and ≥ 2 approved teams; seeds by registration time; byes to top seeds; optional third-place match |
| `GET /api/ops/tournaments/{id}/fixtures` | fixtures with ids, versions and team ids |
| `POST /api/ops/fixtures/{id}/schedule` `{venue, startsAtUtc, endsAtUtc, version}` | venue double-booking, team overlap and 30-min recovery are `409 schedule.clash` |
| `POST /api/ops/fixtures/{id}/start` · `/live` `{period, clock}` | Ready/Scheduled → Live; live clock shown on the public desk |
| `POST /api/ops/fixtures/{id}/result` `{outcome, homeScore, awayScore, walkoverWinnerId, version}` | winner computed server-side; ties rejected; resolved results cannot be overwritten; winner advances; bye chains and forfeits settle; tournament completes automatically |
| `POST /api/ops/teams/{id}/withdraw` | walkovers for opponents; reports matches needing a manual walkover |
| `POST /api/ops/tournaments/{id}/regeneration/preview` · `/confirm` | continuation bracket: history kept, withdrawn/rejected/eliminated excluded, no completed pairing replayed; blocked while a match is live |

## Admin console — READ-ONLY (role Admin)
There is **no endpoint that creates, updates or deletes users, teams, tournaments or fixtures**.
PII (emails, enrollment numbers) is masked by default.

| Endpoint | Purpose |
|---|---|
| `GET /api/admin/dashboard` | counts, role/department breakdowns, registrations per day, alerts inputs |
| `GET /api/admin/users?search=&role=&verified=&createdFrom=&createdTo=&page=&pageSize=` | user directory (email masked) |
| `GET /api/admin/users/{id}?reveal=true` | detail; **every call is audited**, `reveal=true` shows the full email (audited as `admin.user.revealed_email`) |
| `GET /api/admin/participants?search=&tournamentId=&sportId=&department=&teamStatus=` | player-level directory (enrollment masked) |
| `GET /api/admin/teams?search=&tournamentId=&status=` / `GET /api/admin/teams/{id}` | teams and rosters |
| `GET /api/admin/departments` | participation per department |
| `GET /api/admin/audit?action=&actorId=&entityType=&from=&to=` | append-only audit viewer |
| `POST /api/admin/exports/users` · `POST /api/admin/exports/participants` | CSV (formula-injection safe, ≤5 000 rows, 5 per 10 min). Body `{…filters, includeSensitive, reason}`; sensitive export needs a reason ≥ 10 chars. Always audited. |
