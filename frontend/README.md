# Frontend

React 18 · TypeScript · Vite · TanStack Query · React Router · Supabase Auth (Google).

```bash
cd frontend
cp .env.example .env.local        # fill in Supabase + API URL
npm install
npm run dev                       # http://localhost:5173
npm run typecheck && npm test && npm run build
```

## Try it without Supabase (dev only)
```bash
npm run mock-api                  # terminal 1 — in-memory API on :5000 (same contract as docs/API.md)
npm run dev:mock                  # terminal 2 — UI with a role picker: captain / scorer / admin
```
Mock mode shows an orange banner on every page. It is **not** the source of business rules — those live in the C# backend.

## Structure
`src/lib` api client · auth · hooks · format · types — `src/components` UI kit, fixtures/bracket, layouts — `src/pages` public · auth · captain · ops · admin.

Routes: `/` `/sports` `/tournaments(/:id)` `/fixtures` `/leaderboards` `/teams` `/login` `/register` `/auth/callback` `/verify-email` ·
`/captain/*` · `/ops/*` (Scorer) · `/workspace/*` (read-only Admin).
