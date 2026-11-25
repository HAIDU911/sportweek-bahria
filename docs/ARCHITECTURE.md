# Architecture

```
Browser (React/Vite, Supabase Auth client for Google sign-in only)
   │  HTTPS JSON + Supabase access token (Bearer)
   ▼
ASP.NET Core 8 API ── correlation → exception shield → security headers → CORS → authN → rate limit → authZ
   │  JWT: issuer, audience, signature (JWKS/ES256·RS256, or legacy HS256), expiry
   │  Role = f(server allowlists, verified email)  — never from the token
   ▼
Application (use cases, policies, masking, CSV safety, provisioning)   ← pure, unit-tested
Domain (bracket engine, advancement, regeneration, clash detection)    ← pure, 62 tests incl. 2 500 simulations
   ▼
Infrastructure (Dapper + Npgsql, parameterised SQL only, append-only audit)
   ▼
Supabase PostgreSQL  (RLS on every table, safe public views, audit triggers, least-privilege API role)
```

## Frontend (React 18 + TypeScript + Vite)
`frontend/src`: `lib/` (typed API client, auth, hooks, formatting) · `components/` (design-system UI kit, fixtures/bracket, layouts, role guard) · `pages/` (public, auth, captain, ops, admin).
- Four shells: public site, captain workspace, operator desk, read-only admin console. Routes are guarded by `RequireRole`; the server still enforces every permission.
- TanStack Query for server state (5 s polling on live data; no retry on 4xx). One `DataState` component renders loading / empty / error / outage for every query.
- Google sign-in via Supabase Auth (PKCE). The browser holds only the anon key; the role shown in the UI is read from `GET /api/me`, never from the token.
- `tools/check_contract.py` fails CI if TypeScript types drift from the C# DTOs.

## Layer rules
- `Domain` and `Application` have **zero** package dependencies (no HTTP, SQL, Supabase, SMTP).
- The browser never talks to PostgreSQL with a privileged key. Service-role/DB credentials are server-only.
- Public DTOs are explicit projections served from SQL views that physically lack private columns.
- Mutations validate role → ownership → state → input → idempotency, inside one transaction, and audit.

## Key decisions (ADR summary)
| # | Decision | Why |
|---|---|---|
| 1 | Dapper + hand-written SQL instead of EF Core | Predictable queries, trivially reviewable for injection/PII leakage, every statement is executed against real PostgreSQL in CI |
| 2 | Role resolved server-side on each (cached 60 s) request | Role revocation takes effect within a minute; token claims can't escalate |
| 3 | Unverified email ⇒ never elevated, never a team registrant | Prevents allowlist spoofing with unverified addresses |
| 4 | Gmail canonicalisation (dots, `+tag`) | Stops alias bypass of allowlists and duplicate accounts |
| 5 | Audit has **no FK** and is protected by triggers + no UPDATE/DELETE grant | History must survive cascades and compromised API credentials |
| 6 | Admin console is read-only by construction (no write endpoints) | Scope requested: view users and participants only |
| 7 | Manual validators in Application instead of FluentValidation | Keeps Application dependency-free and testable; FluentValidation can wrap request DTOs at the edge later |
| 8 | Tenant-ready (`tenant_id` nullable) but **not** multi-tenant | No isolation/tests yet, so it is not claimed |

## Tournament engine (Domain)
Deterministic single elimination for 2–64 teams with standard seeding, byes (top seeds), bye chains, walkovers, double forfeits,
abandoned matches, optional third-place match, continuation regeneration that excludes withdrawn/rejected/eliminated teams,
includes never-played teams and never replays a completed matchup. `TournamentOpsService` persists and drives it (Scorer role) with optimistic versions, notifications and audit.
