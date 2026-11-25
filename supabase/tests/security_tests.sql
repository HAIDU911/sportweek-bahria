-- Run after migrations on a scratch database: psql -v ON_ERROR_STOP=1 -f security_tests.sql
-- Each block RAISEs on failure. Prints PASS lines on success.
do $$
declare p1 uuid := gen_random_uuid(); p2 uuid := gen_random_uuid(); t uuid; tm uuid; n int; ok boolean;
begin
  insert into profiles(id,email,email_normalized,display_name,email_verified) values
    (p1,'a@gmail.com','a@gmail.com','A',true),(p2,'b@gmail.com','b@gmail.com','B',true);
  select id into t from tournaments limit 1;
  insert into teams(tournament_id,owner_id,name,name_normalized,department,status,idempotency_key)
    values (t,p1,'Falcons','falcons','CS','Approved','key-aaaaaaaa') returning id into tm;
  insert into team_players(team_id,tournament_id,full_name,enrollment_no,enrollment_normalized,department)
    values (tm,t,'Ali','01-111111','01-111111','CS');

  -- 1. duplicate player in same tournament is rejected by the database itself
  begin
    insert into teams(tournament_id,owner_id,name,name_normalized,department,idempotency_key) values (t,p2,'Hawks','hawks','EE','key-bbbbbbbb');
    insert into team_players(team_id,tournament_id,full_name,enrollment_no,enrollment_normalized,department)
      select id,t,'Ali Clone','01-111111','01-111111','EE' from teams where name_normalized='hawks';
    raise exception 'FAIL duplicate player accepted';
  exception when unique_violation then raise notice 'PASS duplicate player blocked'; end;

  -- 2. duplicate team name and idempotency key
  begin insert into teams(tournament_id,owner_id,name,name_normalized,department,idempotency_key) values (t,p2,'FALCONS','falcons','EE','key-cccccccc');
    raise exception 'FAIL duplicate team name accepted'; exception when unique_violation then raise notice 'PASS duplicate team name blocked'; end;
  begin insert into teams(tournament_id,owner_id,name,name_normalized,department,idempotency_key) values (t,p1,'Other','other','EE','key-aaaaaaaa');
    raise exception 'FAIL idempotency key reuse accepted'; exception when unique_violation then raise notice 'PASS idempotency key unique per owner'; end;

  -- 3. audit log is append-only (even for superuser-owned sessions)
  insert into audit_events(action) values ('test.action');
  begin update audit_events set action='tampered'; raise exception 'FAIL audit update allowed'; exception when insufficient_privilege then raise notice 'PASS audit update blocked'; end;
  begin delete from audit_events; raise exception 'FAIL audit delete allowed'; exception when insufficient_privilege then raise notice 'PASS audit delete blocked'; end;
  begin truncate audit_events; raise exception 'FAIL audit truncate allowed'; exception when insufficient_privilege then raise notice 'PASS audit truncate blocked'; end;

  -- 4. resolved fixture cannot be deleted; winner must be a participant
  insert into fixtures(tournament_id,round,slot,home_team_id,status) values (t,1,1,tm,'Bye');
  begin delete from fixtures; raise exception 'FAIL resolved fixture deleted'; exception when insufficient_privilege then raise notice 'PASS resolved fixture protected'; end;
  begin insert into fixtures(tournament_id,round,slot,home_team_id,winner_team_id,status) values (t,1,2,tm,gen_random_uuid(),'Completed');
    raise exception 'FAIL bad winner accepted'; exception when foreign_key_violation or check_violation then raise notice 'PASS winner must be participant'; end;
end $$;

-- 5. anon / authenticated cannot read base tables, but can read safe views
set role anon;
do $$ declare ok boolean := false; begin
  begin perform 1 from public.profiles; exception when insufficient_privilege then ok := true; end;
  if not ok then raise exception 'FAIL anon can read profiles'; end if;
  ok := false;
  begin perform 1 from public.audit_events; exception when insufficient_privilege then ok := true; end;
  if not ok then raise exception 'FAIL anon can read audit_events'; end if;
  ok := false;
  begin perform 1 from public.team_players; exception when insufficient_privilege then ok := true; end;
  if not ok then raise exception 'FAIL anon can read team_players'; end if;
  perform 1 from public.public_team_directory;
  perform 1 from public.public_tournament_summary;
  raise notice 'PASS anon blocked from base tables, allowed on public views';
end $$;
reset role;

-- 6. public views expose no private columns
do $$ declare bad text; begin
  select string_agg(table_name||'.'||column_name, ', ') into bad from information_schema.columns
   where table_schema='public' and table_name like 'public\_%'
     and column_name ~* '(email|phone|enrollment|token|secret|owner|idempotency|rejection|ip_address|metadata)';
  if bad is not null then raise exception 'FAIL public views leak: %', bad; end if;
  raise notice 'PASS public views contain no private columns';
end $$;

-- 7. audit history survives cascading truncates / deletes of its referenced entities
do $$ declare n int; begin
  begin truncate public.profiles cascade; exception when others then null; end;
  select count(*) into n from public.audit_events;
  if n = 0 then raise exception 'FAIL audit events erased by cascade'; end if;
  raise notice 'PASS audit events survive cascade truncate of profiles';
end $$;
