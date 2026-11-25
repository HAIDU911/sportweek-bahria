# Roadmap & honest status

| Phase | Scope | Status |
|---|---|---|
| 0 | Decisions, read-only admin scope, Stitch prompt pack | ✅ |
| 1 | Solution structure, strict compiler settings, health, CI | ✅ (CI file not yet run on GitHub) |
| 2 | Domain engine (brackets, advancement, regeneration, clashes) | ✅ 62 tests incl. ~2 500 simulations |
| 3 | Identity: JWT validation, server-assigned roles | ✅ code + unit tests · ⚠ not tested against real Supabase/Google |
| 4 | Public + captain API (registration, notifications, withdraw) | ✅ |
| 5 | Admin read-only console API | ✅ |
| 5b | **Operator desk**: team review, lifecycle, generation, scheduling, live scoring, withdrawal, regeneration | ✅ 15 service tests + SQL verified on PostgreSQL |
| 6 | **Frontend**: 4 shells, 30+ routes, design system, bracket, role guards | ✅ builds, 23 unit tests, 23 browser flows (against dev mock API) |
| 7 | Email outbox worker (table exists), Realtime push, metrics | ⏳ |
| 8 | Load test (300 users), pen test, restore drill, sign-off | ⏳ |

## Decisions taken
- Team approval and all tournament operations belong to the **Scorer** role. The Admin stays strictly read-only.
- Stakeholder role exists in the backend but has no dashboard yet (lands on the public site).
- Seeding is by registration time (earliest = top seed). A manual seeding screen is a possible later addition.

## Known gaps / next
1. First real `dotnet build` of `Api`/`Infrastructure` (verified only against stubs) and first real Google login.
2. Email notifications (in-app notifications exist).
3. Seed editing, multi-day schedule view for venues, per-sport scoring detail (sets/innings) beyond a single score.
4. Chess/other sports where a draw is valid need a tiebreak rule.
5. Playwright suite currently targets the dev mock API; add a run against the real stack in CI.
