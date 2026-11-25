-- SAFE PUBLIC PROJECTIONS. These are the only objects exposed to the `anon` role.
-- They contain no emails, phones, enrollment numbers, rosters, notifications, audit data or tokens.
-- Views run with the owner's rights (default) so base-table RLS can stay fully closed.

create view public.public_sport_view as
select id, name, slug, description from public.sports where is_active;

create view public.public_tournament_summary as
select t.id, t.name, t.sport_id, s.name as sport_name, t.status, t.registration_open,
       t.starts_at, t.ends_at, t.timezone, t.team_limit,
       (select count(*) from public.teams x where x.tournament_id = t.id and x.status = 'Approved')::int as approved_teams,
       (select count(*) from public.fixtures f where f.tournament_id = t.id and f.status <> 'Bye')::int as fixtures_total,
       (select count(*) from public.fixtures f where f.tournament_id = t.id and f.status in ('Completed','Walkover'))::int as fixtures_completed
from public.tournaments t join public.sports s on s.id = t.sport_id
where t.status <> 'Draft' and s.is_active;

create view public.public_fixture_view as
select f.id, f.tournament_id, f.round, f.slot, f.is_third_place, f.status,
       h.name as home_team, a.name as away_team, f.home_score, f.away_score,
       w.name as winner_team, f.venue, f.starts_at
from public.fixtures f
join public.tournaments t on t.id = f.tournament_id and t.status <> 'Draft'
left join public.teams h on h.id = f.home_team_id
left join public.teams a on a.id = f.away_team_id
left join public.teams w on w.id = f.winner_team_id;

create view public.public_team_directory as
select tm.id, tm.name, tm.department, s.name as sport_name, t.name as tournament_name, t.id as tournament_id
from public.teams tm
join public.tournaments t on t.id = tm.tournament_id
join public.sports s on s.id = t.sport_id
where tm.status = 'Approved' and t.status <> 'Draft';

-- 3 points per win (completed or walkover), attributed to the winning team's department.
create view public.public_leaderboard_view as
select tm.department,
       (count(f.id) * 3)::int as points,
       count(f.id)::int as matches_won,
       (select count(*) from public.teams x where x.department = tm.department and x.status = 'Approved')::int as teams_approved
from public.teams tm
left join public.fixtures f on f.winner_team_id = tm.id and f.status in ('Completed','Walkover')
where tm.status = 'Approved'
group by tm.department;

revoke all on public.public_sport_view, public.public_tournament_summary, public.public_fixture_view,
              public.public_team_directory, public.public_leaderboard_view from public, anon, authenticated;
grant select on public.public_sport_view, public.public_tournament_summary, public.public_fixture_view,
                public.public_team_directory, public.public_leaderboard_view to anon, authenticated;
