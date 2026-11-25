-- Least-privilege database role for the ASP.NET API (replace the password; store it in a secret manager).
-- The API needs read/write on business tables and INSERT-only on audit_events. It never needs DDL or auth schema access.
do $$ begin
  if not exists (select from pg_roles where rolname = 'sportsweek_api') then
    create role sportsweek_api login password 'CHANGE_ME_BEFORE_APPLYING' nosuperuser nocreatedb nocreaterole noinherit;
  end if;
end $$;

grant usage on schema public to sportsweek_api;
grant select, insert, update on public.profiles, public.teams, public.team_players, public.fixtures,
      public.notifications, public.email_outbox to sportsweek_api;
grant select on public.sports, public.tournaments to sportsweek_api;
grant select, insert on public.audit_events to sportsweek_api;          -- append-only: no update/delete grant
grant select on public.public_sport_view, public.public_tournament_summary, public.public_fixture_view,
                public.public_team_directory, public.public_leaderboard_view to sportsweek_api;
grant select, update on public.tournaments to sportsweek_api;           -- row lock (SELECT ... FOR UPDATE) requires UPDATE privilege

-- RLS is enabled; the API role is explicitly allowed through (it is the trusted, authorised backend).
create policy api_all_profiles   on public.profiles      for all to sportsweek_api using (true) with check (true);
create policy api_all_teams      on public.teams         for all to sportsweek_api using (true) with check (true);
create policy api_all_players    on public.team_players  for all to sportsweek_api using (true) with check (true);
create policy api_all_fixtures   on public.fixtures      for all to sportsweek_api using (true) with check (true);
create policy api_all_notif      on public.notifications for all to sportsweek_api using (true) with check (true);
create policy api_all_outbox     on public.email_outbox  for all to sportsweek_api using (true) with check (true);
create policy api_all_sports     on public.sports        for select to sportsweek_api using (true);
create policy api_all_tournament on public.tournaments   for all to sportsweek_api using (true) with check (true);
create policy api_audit_select   on public.audit_events  for select to sportsweek_api using (true);
create policy api_audit_insert   on public.audit_events  for insert to sportsweek_api with check (true);
