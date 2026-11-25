# Security model & threat register

| Threat | Control | Evidence |
|---|---|---|
| Forged / tampered / expired token | Validate issuer, audience, signature, lifetime; allowed algs ES256/RS256/HS256 only; JWKS cache with rotation | `Program.cs` |
| Role escalation via token or request body | Role comes from DB profile ← server allowlists; browser role ignored | `IdentityProvisioningTests` |
| Allowlist spoofing with unverified email | Unverified ⇒ Captain only, cannot register teams | test `Unverified_email_never_receives_an_elevated_role` |
| Gmail alias bypass | Canonicalisation | test `Gmail_dots_plus_tags…` |
| IDOR on captain data | Scope = caller id from token; no id parameter | `CaptainService.ListOwnTeamsAsync` |
| Non-admin reads admin data | Policy + service-level role check (defence in depth) | `Non_admins_are_forbidden_everywhere` |
| PII over-exposure by admins | Masked by default; reveal & sensitive export need explicit action, reason, audit; audit fails closed | `AdminDirectoryServiceTests` |
| CSV formula injection | `'` prefix for `= + - @ \t \r` | `Csv_blocks_formula_injection` |
| SQL injection | Parameters only; LIKE wildcards escaped | all 48 statements run on PostgreSQL; `users '%'` test |
| Duplicate/racing registration | DB unique constraints + row lock + idempotency key | `security_tests.sql` |
| Tampering with audit trail | Triggers block UPDATE/DELETE/TRUNCATE; API role has no such grant; no FK so cascades can't erase it | `security_tests.sql` (found & fixed a cascade hole) |
| Leaked anon key | RLS on every table, no anon policies on base tables; public views omit private columns | `security_tests.sql` |
| Compromised API credential | Least-privilege role (no DDL, no audit update/delete) | role test on PostgreSQL |
| Abuse / scraping / brute force | Rate limits: public 120/min/IP, authenticated 240/min, export 5/10 min | `Program.cs` |
| Info leakage in errors | Global exception shield, stable contract, correlation id only | `ExceptionMiddleware` |
| Clickjacking / sniffing / caching of private data | CSP `default-src 'none'`, X-Frame-Options, nosniff, HSTS, `no-store` on non-public routes | `SecurityHeadersMiddleware` |
| Log/ header injection | Correlation id accepted only if `[A-Za-z0-9_-]{8,64}` | `CorrelationIdMiddleware` |

| Admin tampering with tournament data | Admin role has **no** mutation endpoint; operations are Scorer-only and tested (`Only_scorers_can_run_operations_admin_is_read_only`) | `OpsTests`, e2e |
| Lost updates / double-submit scoring | Optimistic `version` on every fixture/team change, all-or-nothing save | `Stale_version_is_rejected…`, SQL harness |
| Overbooked venue / double-booked team | Clash detector before save | `Scheduling_detects_venue_and_team_clashes…`, e2e |
| Secrets in the browser | Only `VITE_SUPABASE_URL`, anon key and API URL; DB and service keys are server-only | `.env.example` |
| Mock auth leaking into production | Mock mode requires `VITE_AUTH_MODE=mock`, shows a banner on every page, and the real API never accepts `mock-*` tokens | `frontend/src/lib/config.ts` |
| XSS | React escapes output; no `dangerouslySetInnerHTML`; API sends `Content-Security-Policy: default-src 'none'`. Serve the SPA with a CSP (`script-src 'self'`, `connect-src` = API + Supabase) | host config |

## Operational requirements before production (not yet done)
1. Put the API behind a trusted reverse proxy and set `KnownProxies/KnownNetworks`.
2. Rotate the `sportsweek_api` password; store secrets in a secret manager; enable gitleaks (workflow included).
3. Enable Supabase backups + PITR; run and record a restore drill.
4. Pen-test token tampering, OAuth state, IDOR, rate limits against a staging deployment.
5. Dependency scanning (`dotnet list package --vulnerable` in CI) and Dependabot.
6. Host the SPA behind HTTPS with a strict CSP; Supabase stores the session in localStorage, so XSS hygiene matters.
7. 60 s role-cache window: shorten or add pub/sub invalidation if instant revocation is required.
