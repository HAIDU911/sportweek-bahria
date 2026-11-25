-- Audit history is append-only: block UPDATE/DELETE/TRUNCATE for every role, including the API's own credential.
create or replace function public.audit_events_block_mutation() returns trigger
language plpgsql as $$
begin
  raise exception 'audit_events is append-only (% blocked)', tg_op using errcode = '42501';
end $$;

create trigger trg_audit_no_update_delete
  before update or delete on public.audit_events
  for each row execute function public.audit_events_block_mutation();

create trigger trg_audit_no_truncate
  before truncate on public.audit_events
  for each statement execute function public.audit_events_block_mutation();

-- Completed history cannot be deleted by ordinary operations.
create or replace function public.fixtures_protect_history() returns trigger
language plpgsql as $$
begin
  if old.status in ('Completed','Walkover','DoubleForfeit','Abandoned','Bye') then
    raise exception 'resolved fixtures cannot be deleted' using errcode = '42501';
  end if;
  return old;
end $$;

create trigger trg_fixtures_protect_history
  before delete on public.fixtures
  for each row execute function public.fixtures_protect_history();
