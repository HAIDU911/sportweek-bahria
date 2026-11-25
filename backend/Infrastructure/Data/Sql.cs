namespace SportsWeek.Infrastructure.Data;

/// <summary>
/// All SQL lives here as named constants. Every value is passed as a parameter (never concatenated), so there is no SQL injection
/// surface. Optional filters use `@p::type is null or ...` so a null parameter still has a known type in PostgreSQL.
/// </summary>
public static class Sql
{
    // ---------- admin: users ----------
    public const string UsersWhere = """
        where (@search::text is null or p.display_name ilike @pattern::text escape '\' or p.email ilike @pattern::text escape '\')
          and (@role::text is null or p.role = @role::text)
          and (@verified::boolean is null or p.email_verified = @verified::boolean)
          and (@from::timestamptz is null or p.created_at >= @from::timestamptz)
          and (@to::timestamptz is null or p.created_at < @to::timestamptz)
        """;

    public const string UsersCount = "select count(*)::int from public.profiles p " + UsersWhere;

    public const string UsersPage = """
        select p.id, p.display_name, p.email, p.role, p.email_verified, p.is_active,
               (select count(*) from public.teams t where t.owner_id = p.id)::int as teams_owned,
               p.last_sign_in_at as last_sign_in_at_utc, p.created_at as created_at_utc
        from public.profiles p
        """ + "\n" + UsersWhere + """

        order by p.created_at desc, p.id
        limit @size offset @offset
        """;

    public const string UserById = """
        select id, display_name, email, role, role_source, email_verified, is_active,
               coalesce(google_subject, '') as google_subject, last_sign_in_at as last_sign_in_at_utc, created_at as created_at_utc
        from public.profiles where id = @id
        """;

    public const string UserTeams = """
        select t.id as team_id, t.name as team_name, t.status, tr.name as tournament_name, s.name as sport_name
        from public.teams t join public.tournaments tr on tr.id = t.tournament_id join public.sports s on s.id = tr.sport_id
        where t.owner_id = @id order by t.created_at desc limit 50
        """;

    // ---------- admin: participants ----------
    public const string ParticipantsWhere = """
        where (@search::text is null or pl.full_name ilike @pattern::text escape '\' or pl.enrollment_no ilike @pattern::text escape '\'
               or t.name ilike @pattern::text escape '\')
          and (@tournament::uuid is null or t.tournament_id = @tournament::uuid)
          and (@sport::uuid is null or tr.sport_id = @sport::uuid)
          and (@department::text is null or lower(pl.department) = lower(@department::text))
          and (@status::text is null or t.status = @status::text)
        """;

    public const string ParticipantsFrom = """
        from public.team_players pl
        join public.teams t on t.id = pl.team_id
        join public.tournaments tr on tr.id = t.tournament_id
        join public.sports s on s.id = tr.sport_id
        join public.profiles o on o.id = t.owner_id
        """;

    public const string ParticipantsCount = "select count(*)::int " + ParticipantsFrom + "\n" + ParticipantsWhere;

    public const string ParticipantsPage = """
        select pl.id as player_id, pl.full_name, pl.enrollment_no, pl.department, t.id as team_id, t.name as team_name,
               t.status as team_status, s.name as sport_name, tr.name as tournament_name, tr.id as tournament_id,
               o.display_name as captain_name, pl.created_at as registered_at_utc
        """ + "\n" + ParticipantsFrom + "\n" + ParticipantsWhere + """

        order by pl.created_at desc, pl.id
        limit @size offset @offset
        """;

    // ---------- admin: teams ----------
    public const string TeamsWhere = """
        where (@search::text is null or t.name ilike @pattern::text escape '\' or t.department ilike @pattern::text escape '\')
          and (@tournament::uuid is null or t.tournament_id = @tournament::uuid)
          and (@status::text is null or t.status = @status::text)
        """;

    public const string TeamsFrom = """
        from public.teams t
        join public.tournaments tr on tr.id = t.tournament_id
        join public.sports s on s.id = tr.sport_id
        join public.profiles o on o.id = t.owner_id
        """;

    public const string TeamsCount = "select count(*)::int " + TeamsFrom + "\n" + TeamsWhere;

    public const string TeamSelect = """
        select t.id as team_id, t.name as team_name, t.department, t.status, s.name as sport_name, tr.name as tournament_name,
               tr.id as tournament_id, o.display_name as captain_name, o.email as captain_email,
               (select count(*) from public.team_players x where x.team_id = t.id)::int as roster_count,
               t.created_at as submitted_at_utc, t.rejection_reason, t.version
        """;

    public const string TeamsPage = TeamSelect + "\n" + TeamsFrom + "\n" + TeamsWhere + """

        order by t.created_at desc, t.id
        limit @size offset @offset
        """;

    public const string TeamById = TeamSelect + "\n" + TeamsFrom + "\n where t.id = @id";

    public const string TeamRoster = """
        select id as player_id, full_name, enrollment_no, department
        from public.team_players where team_id = @id order by full_name
        """;

    // ---------- admin: dashboard ----------
    public const string DashboardCounts = """
        select
          (select count(*) from public.profiles)::int as total_users,
          (select count(*) from public.profiles where role = 'Captain' and is_active)::int as active_captains,
          (select count(*) from public.teams)::int as total_teams,
          (select count(*) from public.team_players)::int as total_participants,
          (select count(*) from public.tournaments)::int as total_tournaments,
          (select count(*) from public.teams where status = 'Pending')::int as pending_teams,
          (select count(*) from public.teams where status = 'Approved')::int as approved_teams,
          (select count(*) from public.teams where status = 'Rejected')::int as rejected_teams,
          (select count(*) from public.teams where status = 'Withdrawn')::int as withdrawn_teams,
          (select count(*) from public.teams where status = 'Pending' and created_at < now() - interval '48 hours')::int as stale_pending_teams,
          (select count(*) from public.email_outbox where status = 'failed')::int as email_failures
        """;

    public const string UsersByRole = "select role as name, count(*)::int as count from public.profiles group by role order by count desc";

    public const string ParticipantsByDepartment = """
        select department as name, count(*)::int as count from public.team_players
        group by department order by count desc, department limit 15
        """;

    public const string RegistrationsPerDay = """
        select date_trunc('day', created_at) as day_utc, count(*)::int as count
        from public.teams where created_at >= now() - interval '14 days'
        group by 1 order by 1
        """;

    public const string DepartmentStats = """
        select pl.department, count(*)::int as participants, count(distinct pl.team_id)::int as teams,
               count(distinct pl.team_id) filter (where t.status = 'Approved')::int as approved_teams
        from public.team_players pl join public.teams t on t.id = pl.team_id
        group by pl.department order by participants desc, pl.department
        """;

    // ---------- audit ----------
    public const string AuditWhere = """
        where (@action::text is null or a.action = @action::text)
          and (@actor::uuid is null or a.actor_id = @actor::uuid)
          and (@entity::text is null or a.entity_type = @entity::text)
          and (@from::timestamptz is null or a.occurred_at >= @from::timestamptz)
          and (@to::timestamptz is null or a.occurred_at < @to::timestamptz)
        """;

    public const string AuditSelect = """
        select a.id, a.occurred_at as occurred_at_utc, a.actor_id, p.display_name as actor_name, p.role as actor_role,
               a.action, a.entity_type, a.entity_id, a.correlation_id, a.result, a.metadata::text as metadata_json
        from public.audit_events a left join public.profiles p on p.id = a.actor_id
        """;

    public const string AuditCount = "select count(*)::int from public.audit_events a " + AuditWhere;

    public const string AuditPage = AuditSelect + "\n" + AuditWhere + """

        order by a.occurred_at desc, a.id
        limit @size offset @offset
        """;

    public const string AuditForUser = AuditSelect + "\n where a.actor_id = @id order by a.occurred_at desc limit 20";

    public const string AuditInsert = """
        insert into public.audit_events (action, actor_id, entity_type, entity_id, result, correlation_id, ip_address, metadata)
        values (@action, @actor, @entityType, @entityId, @result, @correlationId, @ip, @metadata::jsonb)
        """;

    // ---------- identity ----------
    public const string ProfileRole = "select role from public.profiles where id = @id";

    public const string ProfileUpsert = """
        insert into public.profiles (id, google_subject, email, email_normalized, display_name, avatar_url, role, role_source, email_verified, last_sign_in_at)
        values (@id, @googleSubject, @email, @emailNormalized, @displayName, @avatarUrl, @role, @roleSource, @emailVerified, now())
        on conflict (id) do update set
          google_subject = coalesce(excluded.google_subject, public.profiles.google_subject),
          email = excluded.email, email_normalized = excluded.email_normalized,
          display_name = excluded.display_name, avatar_url = excluded.avatar_url,
          email_verified = excluded.email_verified, role = excluded.role, role_source = excluded.role_source,
          last_sign_in_at = now(), updated_at = now()
        returning id, email, display_name, role, email_verified, is_active
        """;

    // ---------- captain ----------
    public const string TournamentRules = """
        select tr.id, tr.status, tr.registration_open, tr.team_limit, s.min_players, s.max_players,
               (select count(*) from public.teams x where x.tournament_id = tr.id and x.status in ('Pending','Approved'))::int as active_team_count
        from public.tournaments tr join public.sports s on s.id = tr.sport_id where tr.id = @id
        """;

    public const string TournamentLock = "select team_limit from public.tournaments where id = @id for update";
    public const string ActiveTeamCount = "select count(*)::int from public.teams where tournament_id = @id and status in ('Pending','Approved')";
    public const string TeamByIdempotency = "select id from public.teams where owner_id = @owner and idempotency_key = @key";

    public const string TeamInsert = """
        insert into public.teams (tournament_id, owner_id, name, name_normalized, department, idempotency_key)
        values (@tournament, @owner, @name, @nameNormalized, @department, @key) returning id
        """;

    public const string PlayerInsert = """
        insert into public.team_players (team_id, tournament_id, full_name, enrollment_no, enrollment_normalized, department)
        values (@team, @tournament, @fullName, @enrollmentNo, @enrollmentNormalized, @department)
        """;

    public const string ConflictingEnrollments = """
        select enrollment_no from public.team_players
        where tournament_id = @tournament and enrollment_normalized = any(@normalized)
        """;

    public const string OwnedTeams = """
        select t.id as team_id, t.name as team_name, t.department, t.status, t.rejection_reason, tr.id as tournament_id,
               tr.name as tournament_name, s.name as sport_name,
               (select count(*) from public.team_players x where x.team_id = t.id)::int as roster_count,
               t.created_at as submitted_at_utc
        from public.teams t join public.tournaments tr on tr.id = t.tournament_id join public.sports s on s.id = tr.sport_id
        where t.owner_id = @owner order by t.created_at desc
        """;

    // ---------- public ----------
    public const string PublicSports = "select id, name, slug, description from public.public_sport_view order by name";

    public const string PublicTournamentCols = """
        select id, name, sport_id, sport_name, status, registration_open as registration_open, starts_at as starts_at_utc,
               ends_at as ends_at_utc, timezone, approved_teams, team_limit, fixtures_total, fixtures_completed, min_players, max_players
        from public.public_tournament_summary
        """;

    public const string PublicTournamentCount = "select count(*)::int from public.public_tournament_summary where (@status::text is null or status = @status::text)";
    public const string PublicTournamentPage = PublicTournamentCols + """

        where (@status::text is null or status = @status::text)
        order by starts_at nulls last, name limit @size offset @offset
        """;
    public const string PublicTournamentById = PublicTournamentCols + "\n where id = @id";

    public const string PublicFixtureCount = "select count(*)::int from public.public_fixture_view where tournament_id = @id";
    public const string PublicFixturePage = """
        select id, round, slot, is_third_place, status, home_team, away_team, home_score, away_score, winner_team, venue, starts_at as starts_at_utc
        from public.public_fixture_view where tournament_id = @id
        order by round, slot, is_third_place limit @size offset @offset
        """;

    public const string PublicTeamsWhere = "where (@search::text is null or name ilike @pattern::text escape '\\' or department ilike @pattern::text escape '\\')";
    public const string PublicTeamCount = "select count(*)::int from public.public_team_directory " + PublicTeamsWhere;
    public const string PublicTeamPage = "select id, name, department, sport_name, tournament_name from public.public_team_directory "
        + PublicTeamsWhere + " order by name, id limit @size offset @offset";

    public const string PublicLeaderboard = "select department, points, matches_won, teams_approved from public.public_leaderboard_view order by points desc, department limit 100";

    // ---------- operations (scorer) ----------
    public const string OpsTeam = """
        select t.id, t.owner_id, t.name, t.status, t.version, t.tournament_id, tr.status as tournament_status
        from public.teams t join public.tournaments tr on tr.id = t.tournament_id where t.id = @id
        """;
    public const string OpsTeamSetStatus = """
        update public.teams set status = @status, rejection_reason = @reason, version = version + 1, updated_at = now()
        where id = @id and (@expected::int < 0 or version = @expected::int)
        """;
    public const string OpsTournament = "select id, name, status, third_place_match as third_place, match_duration_min from public.tournaments where id = @id";
    public const string OpsTournamentCreate = """
        insert into public.tournaments (sport_id, name, starts_at, ends_at, team_limit, third_place_match)
        values (@sport, @name, @starts, @ends, @limit, @third)
        on conflict (sport_id, name) do nothing returning id
        """;
    public const string OpsTournamentStatus = """
        update public.tournaments set status = @status, registration_open = (@status::text = 'RegistrationOpen'), version = version + 1 where id = @id
        """;
    public const string OpsTeams = "select id, owner_id, name, status, created_at as created_at_utc from public.teams where tournament_id = @id";
    public const string OpsTournaments = """
        select tr.id, tr.name, s.name as sport_name, tr.status, tr.team_limit,
               (select count(*) from public.teams x where x.tournament_id = tr.id and x.status = 'Approved')::int as approved_teams,
               (select count(*) from public.teams x where x.tournament_id = tr.id and x.status = 'Pending')::int as pending_teams,
               (select count(*) from public.fixtures f where f.tournament_id = tr.id)::int as fixtures_total,
               (select count(*) from public.fixtures f where f.tournament_id = tr.id and f.status in ('Completed','Walkover','DoubleForfeit','Abandoned','Bye'))::int as fixtures_resolved
        from public.tournaments tr join public.sports s on s.id = tr.sport_id order by tr.created_at desc
        """;
    public const string OpsFixtureTournament = "select tournament_id from public.fixtures where id = @id";
    public const string OpsFixtures = """
        select id, round, slot, is_third_place, status, home_team_id, away_team_id, winner_team_id, home_score, away_score,
               next_round, next_slot, next_is_home, loser_next_round, loser_next_slot, loser_next_is_home,
               version, venue, starts_at as starts_at_utc, ends_at as ends_at_utc
        from public.fixtures where tournament_id = @id
        """;
    public const string OpsFixtureInsert = """
        insert into public.fixtures (id, tournament_id, round, slot, is_third_place, home_team_id, away_team_id, status, winner_team_id,
               home_score, away_score, next_round, next_slot, next_is_home, loser_next_round, loser_next_slot, loser_next_is_home)
        values (@id, @tournament, @round, @slot, @third, @home, @away, @status, @winner, @hs, @as, @nr, @ns, @nh, @lr, @ls, @lh)
        """;
    public const string OpsFixtureDeleteUnresolved = "delete from public.fixtures where tournament_id = @id and status in ('Pending','Ready','Scheduled')";
    public const string OpsFixtureSave = """
        update public.fixtures set home_team_id = @home, away_team_id = @away, status = @status, winner_team_id = @winner,
               home_score = @hs, away_score = @as, version = version + 1, updated_at = now()
        where id = @id and version = @expected
        """;
    public const string OpsSchedule = """
        update public.fixtures set venue = @venue, starts_at = @start, ends_at = @end, status = 'Scheduled', version = version + 1, updated_at = now()
        where id = @id and version = @expected and status in ('Ready','Scheduled')
        """;
    public const string OpsLiveDetail = "update public.fixtures set detail = jsonb_build_object('period', @period::text, 'clock', @clock::text), updated_at = now() where id = @id and status = 'Live'";
    public const string OpsSlots = """
        select id, home_team_id, away_team_id, venue, starts_at as start_utc, ends_at as end_utc from public.fixtures
        where starts_at is not null and ends_at is not null and status in ('Scheduled','Live') and starts_at < @to and ends_at > @from
        """;
    public const string NotificationInsert = """
        insert into public.notifications (user_id, type, title, body, entity_type, entity_id) values (@user, @type, @title, @body, @entityType, @entityId)
        """;

    // ---------- captain extras ----------
    public const string Notifications = """
        select id, type, title, body, entity_type, entity_id, read_at as read_at_utc, created_at as created_at_utc
        from public.notifications where user_id = @user order by created_at desc limit @limit
        """;
    public const string NotificationsMarkRead = "update public.notifications set read_at = now() where user_id = @user and read_at is null";

    public const string FeedCols = """
        select id, tournament_id, tournament_name, sport_name, round, slot, is_third_place, status, home_team, home_department, away_team, away_department,
               home_score, away_score, winner_team, venue, starts_at as starts_at_utc, live_period, live_clock
        from public.public_fixture_feed f
        """;
    public const string OwnerFixtures = FeedCols + """

        where exists (select 1 from public.teams t where t.owner_id = @owner and t.tournament_id = f.tournament_id and t.name in (f.home_team, f.away_team))
        order by f.starts_at nulls last, f.round limit 100
        """;

    // ---------- public home / feed / standings ----------
    public const string HomeStats = """
        select (select count(*) from public.sports where is_active)::int as sports_active,
               (select count(*) from public.teams x join public.tournaments tr on tr.id = x.tournament_id where x.status = 'Approved' and tr.status <> 'Draft')::int as teams_approved,
               (select count(*) from public.fixtures where status in ('Completed','Walkover'))::int as matches_played,
               (select count(distinct department) from public.teams where status = 'Approved')::int as departments,
               (select count(*) from public.fixtures where status = 'Live')::int as live_now
        """;
    public const string HomeLive = FeedCols + "\n where f.status = 'Live' order by f.updated_at desc limit 6";
    public const string HomeToday = FeedCols + """

        where (f.starts_at at time zone 'Asia/Karachi')::date = (now() at time zone 'Asia/Karachi')::date
        order by f.starts_at, f.round limit 40
        """;
    public const string HomeLatest = FeedCols + "\n where f.status in ('Completed','Walkover') order by f.updated_at desc limit 6";
    public const string HomeNextUp = FeedCols + "\n where f.status in ('Scheduled','Ready') and f.starts_at > now() order by f.starts_at limit 1";
    public const string LeaderboardTop5 = "select department, points, matches_won, teams_approved from public.public_leaderboard_view order by points desc, department limit 5";

    public const string FeedWhere = """
        where (@status::text is null or f.status = @status::text)
          and (@tournament::uuid is null or f.tournament_id = @tournament::uuid)
          and (@sport::uuid is null or exists (select 1 from public.tournaments tr where tr.id = f.tournament_id and tr.sport_id = @sport::uuid))
          and (@day::date is null or (f.starts_at at time zone 'Asia/Karachi')::date = @day::date)
        """;
    public const string FeedCount = "select count(*)::int from public.public_fixture_feed f " + FeedWhere;
    public const string FeedPage = FeedCols + "\n" + FeedWhere + "\n order by f.starts_at nulls last, f.round, f.slot limit @size offset @offset";

    public const string Standings = """
        select t.name as team, t.department,
               (count(f.id) filter (where f.status in ('Completed','Walkover')))::int as played,
               (count(f.id) filter (where f.winner_team_id = t.id))::int as wins,
               (count(f.id) filter (where f.status in ('Completed','Walkover') and f.winner_team_id <> t.id))::int as losses
        from public.teams t
        left join public.fixtures f on f.tournament_id = t.tournament_id and t.id in (f.home_team_id, f.away_team_id)
        where t.tournament_id = @id and t.status = 'Approved'
        group by t.id, t.name, t.department order by wins desc, played, t.name
        """;
}
