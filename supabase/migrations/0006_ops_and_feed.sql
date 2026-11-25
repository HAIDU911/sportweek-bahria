-- Operations support: explicit bracket links on fixtures, richer public live feed, notification/ops grants.
alter table public.fixtures
  add column next_round int, add column next_slot int, add column next_is_home boolean not null default false,
  add column loser_next_round int, add column loser_next_slot int, add column loser_next_is_home boolean not null default false,
  add column updated_at timestamptz not null default now();

create index ix_fixtures_status_time on public.fixtures(status, starts_at);

-- Live/today/latest feed for the public home page. No private columns.
create view public.public_fixture_feed as
select f.id, f.tournament_id, t.name as tournament_name, s.name as sport_name, f.round, f.slot, f.is_third_place, f.status,
       h.name as home_team, h.department as home_department, a.name as away_team, a.department as away_department,
       f.home_score, f.away_score, w.name as winner_team, f.venue, f.starts_at,
       f.detail->>'period' as live_period, f.detail->>'clock' as live_clock,
       f.updated_at
from public.fixtures f
join public.tournaments t on t.id = f.tournament_id and t.status <> 'Draft'
join public.sports s on s.id = t.sport_id
left join public.teams h on h.id = f.home_team_id
left join public.teams a on a.id = f.away_team_id
left join public.teams w on w.id = f.winner_team_id
where f.status <> 'Bye';

revoke all on public.public_fixture_feed from public, anon, authenticated;
grant select on public.public_fixture_feed to anon, authenticated, sportsweek_api;

-- Regeneration deletes only UNRESOLVED fixtures (trigger fixtures_protect_history blocks resolved ones).
grant delete on public.fixtures to sportsweek_api;
grant insert on public.tournaments to sportsweek_api;
grant insert on public.notifications to sportsweek_api;

-- Roster limits are public information the registration form needs (min/max players of the sport).
create or replace view public.public_tournament_summary as
select t.id, t.name, t.sport_id, s.name as sport_name, t.status, t.registration_open,
       t.starts_at, t.ends_at, t.timezone, t.team_limit,
       (select count(*) from public.teams x where x.tournament_id = t.id and x.status = 'Approved')::int as approved_teams,
       (select count(*) from public.fixtures f where f.tournament_id = t.id and f.status <> 'Bye')::int as fixtures_total,
       (select count(*) from public.fixtures f where f.tournament_id = t.id and f.status in ('Completed','Walkover'))::int as fixtures_completed,
       s.min_players, s.max_players
from public.tournaments t join public.sports s on s.id = t.sport_id
where t.status <> 'Draft' and s.is_active;
