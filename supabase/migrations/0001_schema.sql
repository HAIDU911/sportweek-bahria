-- SportsWeek Manager — core schema (Supabase PostgreSQL)
-- Conventions: UUID keys, UTC timestamptz, restrictive FKs for history, optimistic `version`,
-- nullable tenant_id on tenant-owned tables (tenant-READY only; NOT multi-tenant until isolation is implemented & tested).

create extension if not exists pgcrypto;

create table public.profiles (
  id               uuid primary key,                      -- = Supabase auth user id (JWT "sub")
  google_subject   text unique,
  email            text not null,
  email_normalized text not null unique,                  -- canonical (gmail dots/+tags removed) → blocks duplicate/alias accounts
  display_name     text not null,
  avatar_url       text,
  role             text not null default 'Captain' check (role in ('Captain','Scorer','Stakeholder','Admin')),
  role_source      text not null default 'default:captain',
  email_verified   boolean not null default false,
  is_active        boolean not null default true,
  last_sign_in_at  timestamptz,
  tenant_id        uuid,
  created_at       timestamptz not null default now(),
  updated_at       timestamptz not null default now()
);
create index ix_profiles_role on public.profiles(role);
create index ix_profiles_created on public.profiles(created_at desc);

create table public.sports (
  id          uuid primary key default gen_random_uuid(),
  name        text not null unique,
  slug        text not null unique,
  description text,
  min_players int not null default 1 check (min_players >= 1),
  max_players int not null default 15 check (max_players >= min_players),
  scoring_kind text not null default 'generic',
  is_active   boolean not null default true,
  tenant_id   uuid,
  created_at  timestamptz not null default now()
);

create table public.tournaments (
  id                 uuid primary key default gen_random_uuid(),
  sport_id           uuid not null references public.sports(id) on delete restrict,
  name               text not null,
  status             text not null default 'Draft'
                     check (status in ('Draft','RegistrationOpen','RegistrationClosed','InProgress','Completed','Cancelled')),
  registration_open  boolean not null default false,
  starts_at          timestamptz,
  ends_at            timestamptz,
  timezone           text not null default 'Asia/Karachi',
  team_limit         int check (team_limit is null or team_limit between 2 and 64),
  third_place_match  boolean not null default false,
  match_duration_min int not null default 60,
  version            int not null default 1,              -- optimistic concurrency
  tenant_id          uuid,
  created_at         timestamptz not null default now(),
  unique (sport_id, name)
);
create index ix_tournaments_status on public.tournaments(status);

create table public.teams (
  id               uuid primary key default gen_random_uuid(),
  tournament_id    uuid not null references public.tournaments(id) on delete restrict,
  owner_id         uuid not null references public.profiles(id) on delete restrict,
  name             text not null,
  name_normalized  text not null,
  department       text not null,
  status           text not null default 'Pending' check (status in ('Pending','Approved','Rejected','Withdrawn')),
  rejection_reason text,
  idempotency_key  text not null,
  version          int not null default 1,
  tenant_id        uuid,
  created_at       timestamptz not null default now(),
  updated_at       timestamptz not null default now(),
  unique (tournament_id, name_normalized),                -- duplicate team name in a tournament
  unique (owner_id, idempotency_key)                      -- safe retries
);
create index ix_teams_tournament_status on public.teams(tournament_id, status);
create index ix_teams_owner on public.teams(owner_id);
create index ix_teams_created on public.teams(created_at desc);

create table public.team_players (
  id                    uuid primary key default gen_random_uuid(),
  team_id               uuid not null references public.teams(id) on delete restrict,
  tournament_id         uuid not null references public.tournaments(id) on delete restrict,
  full_name             text not null,
  enrollment_no         text not null,
  enrollment_normalized text not null,
  department            text not null,
  tenant_id             uuid,
  created_at            timestamptz not null default now(),
  unique (tournament_id, enrollment_normalized)           -- one player, one team per tournament
);
create index ix_players_team on public.team_players(team_id);
create index ix_players_department on public.team_players(department);

create table public.fixtures (
  id              uuid primary key default gen_random_uuid(),
  tournament_id   uuid not null references public.tournaments(id) on delete restrict,
  round           int not null check (round >= 1),
  slot            int not null check (slot >= 1),
  is_third_place  boolean not null default false,
  home_team_id    uuid references public.teams(id) on delete restrict,
  away_team_id    uuid references public.teams(id) on delete restrict,
  status          text not null default 'Pending'
                  check (status in ('Pending','Ready','Scheduled','Live','Completed','Walkover','DoubleForfeit','Abandoned','Bye')),
  winner_team_id  uuid references public.teams(id) on delete restrict,
  home_score      int check (home_score is null or home_score >= 0),
  away_score      int check (away_score is null or away_score >= 0),
  detail          jsonb not null default '{}'::jsonb,     -- sport-specific scoring detail
  venue           text,
  starts_at       timestamptz,
  ends_at         timestamptz,
  version         int not null default 1,
  tenant_id       uuid,
  created_at      timestamptz not null default now(),
  unique (tournament_id, round, slot, is_third_place),
  check (home_team_id is null or away_team_id is null or home_team_id <> away_team_id),
  check (winner_team_id is null or winner_team_id in (home_team_id, away_team_id))
);
create index ix_fixtures_tournament_status on public.fixtures(tournament_id, status);
create index ix_fixtures_time on public.fixtures(starts_at);

create table public.audit_events (
  id             uuid primary key default gen_random_uuid(),
  occurred_at    timestamptz not null default now(),
  actor_id       uuid,                                    -- deliberately NO foreign key: audit history must survive profile deletion/cascades
  action         text not null,
  entity_type    text,
  entity_id      text,
  result         text not null default 'success',
  correlation_id text,
  ip_address     text,
  metadata       jsonb,
  tenant_id      uuid
);
create index ix_audit_time on public.audit_events(occurred_at desc);
create index ix_audit_actor on public.audit_events(actor_id, occurred_at desc);
create index ix_audit_action on public.audit_events(action, occurred_at desc);

create table public.notifications (
  id          uuid primary key default gen_random_uuid(),
  user_id     uuid not null references public.profiles(id) on delete cascade,
  type        text not null,
  title       text not null,
  body        text,
  entity_type text,
  entity_id   text,
  read_at     timestamptz,
  tenant_id   uuid,
  created_at  timestamptz not null default now()
);
create index ix_notifications_user on public.notifications(user_id, created_at desc);

create table public.email_outbox (
  id               uuid primary key default gen_random_uuid(),
  to_email         text not null,
  template         text not null,
  payload          jsonb not null default '{}'::jsonb,
  status           text not null default 'queued' check (status in ('queued','sent','failed')),
  retry_count      int not null default 0,
  next_attempt_at  timestamptz not null default now(),
  idempotency_key  text not null unique,
  entity_type      text,
  entity_id        text,
  failure_reason   text,                                  -- redacted: never store secrets/stack traces
  created_at       timestamptz not null default now(),
  sent_at          timestamptz
);
create index ix_outbox_due on public.email_outbox(status, next_attempt_at);
