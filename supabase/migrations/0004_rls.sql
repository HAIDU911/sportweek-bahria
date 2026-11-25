-- Row Level Security: defence in depth. ASP.NET authorization remains mandatory.
-- Base tables are CLOSED to anon/authenticated (RLS on, no permissive policy), so a leaked anon key reads nothing.
-- The API connects with a server-only credential; the Supabase service-role key never reaches a browser.

alter table public.profiles       enable row level security;
alter table public.sports         enable row level security;
alter table public.tournaments    enable row level security;
alter table public.teams          enable row level security;
alter table public.team_players   enable row level security;
alter table public.fixtures       enable row level security;
alter table public.audit_events   enable row level security;
alter table public.notifications  enable row level security;
alter table public.email_outbox   enable row level security;

alter table public.profiles       force row level security;
alter table public.audit_events   force row level security;

revoke all on all tables in schema public from anon, authenticated;
grant select on public.public_sport_view, public.public_tournament_summary, public.public_fixture_view,
                public.public_team_directory, public.public_leaderboard_view to anon, authenticated;

-- A signed-in user may read ONLY their own profile and notifications directly (e.g. via Supabase client).
grant select on public.profiles, public.notifications to authenticated;
create policy profiles_self_read on public.profiles for select to authenticated using (id = auth.uid());
create policy notifications_self_read on public.notifications for select to authenticated using (user_id = auth.uid());
-- Intentionally NO write policies for anon/authenticated: all mutations go through the ASP.NET API.
