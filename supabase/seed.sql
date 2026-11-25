-- DEVELOPMENT / DEMO DATA ONLY. Never run against production. All rows are clearly fake.
insert into public.sports (name, slug, description, min_players, max_players, scoring_kind) values
 ('Futsal','futsal','5-a-side indoor football',5,10,'goals'),
 ('Cricket','cricket','Knockout cricket',11,15,'cricket'),
 ('Tug of War','tug-of-war','8 vs 8 rope pulling',8,10,'rounds'),
 ('Chess','chess','Team chess',3,5,'chess'),
 ('Badminton','badminton','Team badminton',2,4,'games'),
 ('Table Tennis','table-tennis','Team table tennis',2,4,'games')
on conflict do nothing;

insert into public.tournaments (sport_id, name, status, registration_open, starts_at, ends_at, team_limit, third_place_match)
select id, name || ' — Sports Week 2026 (DEMO)', 'RegistrationOpen', true,
       now() + interval '7 days', now() + interval '12 days', 16, true
from public.sports
on conflict do nothing;
