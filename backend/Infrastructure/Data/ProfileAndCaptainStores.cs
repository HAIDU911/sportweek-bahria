using Dapper;
using Npgsql;
using SportsWeek.Application.Abstractions;
using SportsWeek.Contracts;
using SportsWeek.Domain.Enums;

namespace SportsWeek.Infrastructure.Data;

public sealed class ProfileStore : IProfileStore
{
    private readonly NpgsqlDataSource _ds;
    public ProfileStore(NpgsqlDataSource ds) => _ds = ds;

    private sealed record ProfileRow(Guid Id, string Email, string DisplayName, string Role, bool EmailVerified, bool IsActive);

    public async Task<UpsertResult> UpsertAsync(ProfileUpsert p, CancellationToken ct)
    {
        await using var conn = await _ds.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var prev = await conn.ExecuteScalarAsync<string?>(Q.Cmd(Sql.ProfileRole, new { id = p.Id }, ct, tx));
        try
        {
            var row = await conn.QuerySingleAsync<ProfileRow>(Q.Cmd(Sql.ProfileUpsert, new
            {
                id = p.Id, googleSubject = p.GoogleSubject, email = p.Email, emailNormalized = p.EmailNormalized,
                displayName = p.DisplayName, avatarUrl = p.AvatarUrl, role = p.Role.ToString(), roleSource = p.RoleSource,
                emailVerified = p.EmailVerified
            }, ct, tx));
            await tx.CommitAsync(ct);
            UserRole? previous = prev is not null && Enum.TryParse<UserRole>(prev, out var r) ? r : null;
            return new UpsertResult(UpsertOutcome.Ok, new ProfileSnapshot(row.Id, row.Email, row.DisplayName,
                Enum.Parse<UserRole>(row.Role), row.EmailVerified, row.IsActive, IsNew: prev is null, previous));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await tx.RollbackAsync(ct); // same mailbox already registered under a different auth user
            return new UpsertResult(UpsertOutcome.EmailBelongsToAnotherAccount, null);
        }
    }
}

public sealed class CaptainStore : ICaptainStore
{
    private readonly NpgsqlDataSource _ds;
    public CaptainStore(NpgsqlDataSource ds) => _ds = ds;

    internal static string NormalizeName(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    public async Task<TournamentRules?> GetTournamentRulesAsync(Guid tournamentId, CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        return await c.QuerySingleOrDefaultAsync<TournamentRules>(Q.Cmd(Sql.TournamentRules, new { id = tournamentId }, ct));
    }

    public async Task<CreateTeamResult> CreateTeamAsync(CreateTeamCommand cmd, CancellationToken ct)
    {
        await using var conn = await _ds.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            var existing = await conn.ExecuteScalarAsync<Guid?>(Q.Cmd(Sql.TeamByIdempotency, new { owner = cmd.OwnerId, key = cmd.IdempotencyKey }, ct, tx));
            if (existing is { } id) { await tx.RollbackAsync(ct); return new(CreateTeamOutcome.IdempotentReplay, id); }

            // Row lock serialises concurrent registrations so the team limit cannot be exceeded by a race.
            var limit = await conn.ExecuteScalarAsync<int?>(Q.Cmd(Sql.TournamentLock, new { id = cmd.TournamentId }, ct, tx));
            var active = await conn.ExecuteScalarAsync<int>(Q.Cmd(Sql.ActiveTeamCount, new { id = cmd.TournamentId }, ct, tx));
            if (limit is { } l && active >= l) { await tx.RollbackAsync(ct); return new(CreateTeamOutcome.LimitReached, null); }

            var teamId = await conn.ExecuteScalarAsync<Guid>(Q.Cmd(Sql.TeamInsert, new
            {
                tournament = cmd.TournamentId, owner = cmd.OwnerId, name = cmd.TeamName,
                nameNormalized = NormalizeName(cmd.TeamName), department = cmd.Department, key = cmd.IdempotencyKey
            }, ct, tx));

            foreach (var pl in cmd.Players)
                await conn.ExecuteAsync(Q.Cmd(Sql.PlayerInsert, new
                {
                    team = teamId, tournament = cmd.TournamentId, fullName = pl.FullName, enrollmentNo = pl.EnrollmentNo,
                    enrollmentNormalized = pl.EnrollmentNo.Trim().ToUpperInvariant(), department = pl.Department
                }, ct, tx));

            await tx.CommitAsync(ct);
            return new(CreateTeamOutcome.Created, teamId);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await tx.RollbackAsync(ct);
            return await ClassifyAsync(cmd, ex.ConstraintName ?? string.Empty, ct);
        }
    }

    // The database unique constraints are the source of truth for duplicates; we only translate them to outcomes.
    private async Task<CreateTeamResult> ClassifyAsync(CreateTeamCommand cmd, string constraint, CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        if (constraint.Contains("idempotency", StringComparison.Ordinal))
        {
            var id = await c.ExecuteScalarAsync<Guid?>(Q.Cmd(Sql.TeamByIdempotency, new { owner = cmd.OwnerId, key = cmd.IdempotencyKey }, ct));
            return id is { } g ? new(CreateTeamOutcome.IdempotentReplay, g) : new(CreateTeamOutcome.DuplicateTeamName, null);
        }
        if (constraint.Contains("enrollment_normalized", StringComparison.Ordinal))
        {
            var norm = cmd.Players.Select(p => p.EnrollmentNo.Trim().ToUpperInvariant()).ToArray();
            var hits = (await c.QueryAsync<string>(Q.Cmd(Sql.ConflictingEnrollments, new { tournament = cmd.TournamentId, normalized = norm }, ct))).AsList();
            return new(CreateTeamOutcome.DuplicatePlayers, null, hits);
        }
        return new(CreateTeamOutcome.DuplicateTeamName, null);
    }

    public async Task<IReadOnlyList<CaptainTeamDto>> ListOwnedTeamsAsync(Guid ownerId, CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        return (await c.QueryAsync<CaptainTeamDto>(Q.Cmd(Sql.OwnedTeams, new { owner = ownerId }, ct))).AsList();
    }
}

public sealed class PublicReadStore : IPublicReadStore
{
    private readonly NpgsqlDataSource _ds;
    public PublicReadStore(NpgsqlDataSource ds) => _ds = ds;

    public async Task<IReadOnlyList<PublicSportDto>> ListSportsAsync(CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        return (await c.QueryAsync<PublicSportDto>(Q.Cmd(Sql.PublicSports, null, ct))).AsList();
    }

    public async Task<PagedResult<PublicTournamentDto>> ListTournamentsAsync(int page, int pageSize, string? status, CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        var p = new { status, size = pageSize, offset = (page - 1) * pageSize };
        var total = await c.ExecuteScalarAsync<int>(Q.Cmd(Sql.PublicTournamentCount, p, ct));
        return new(( await c.QueryAsync<PublicTournamentDto>(Q.Cmd(Sql.PublicTournamentPage, p, ct))).AsList(), page, pageSize, total);
    }

    public async Task<PublicTournamentDto?> GetTournamentAsync(Guid id, CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        return await c.QuerySingleOrDefaultAsync<PublicTournamentDto>(Q.Cmd(Sql.PublicTournamentById, new { id }, ct));
    }

    public async Task<PagedResult<PublicFixtureDto>> ListFixturesAsync(Guid tournamentId, int page, int pageSize, CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        var p = new { id = tournamentId, size = pageSize, offset = (page - 1) * pageSize };
        var total = await c.ExecuteScalarAsync<int>(Q.Cmd(Sql.PublicFixtureCount, p, ct));
        return new((await c.QueryAsync<PublicFixtureDto>(Q.Cmd(Sql.PublicFixturePage, p, ct))).AsList(), page, pageSize, total);
    }

    public async Task<PagedResult<PublicTeamDto>> ListTeamsAsync(string? search, int page, int pageSize, CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        var p = new { search, pattern = Q.Pattern(search), size = pageSize, offset = (page - 1) * pageSize };
        var total = await c.ExecuteScalarAsync<int>(Q.Cmd(Sql.PublicTeamCount, p, ct));
        return new((await c.QueryAsync<PublicTeamDto>(Q.Cmd(Sql.PublicTeamPage, p, ct))).AsList(), page, pageSize, total);
    }

    public async Task<IReadOnlyList<PublicLeaderboardRow>> GetOverallLeaderboardAsync(CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        return (await c.QueryAsync<PublicLeaderboardRow>(Q.Cmd(Sql.PublicLeaderboard, null, ct))).AsList();
    }

    public async Task<HomeDto> GetHomeAsync(CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        var stats = await c.QuerySingleAsync<PublicStatsDto>(Q.Cmd(Sql.HomeStats, null, ct));
        var live = (await c.QueryAsync<FeedFixtureDto>(Q.Cmd(Sql.HomeLive, null, ct))).AsList();
        var today = (await c.QueryAsync<FeedFixtureDto>(Q.Cmd(Sql.HomeToday, null, ct))).AsList();
        var latest = (await c.QueryAsync<FeedFixtureDto>(Q.Cmd(Sql.HomeLatest, null, ct))).AsList();
        var next = await c.QuerySingleOrDefaultAsync<FeedFixtureDto>(Q.Cmd(Sql.HomeNextUp, null, ct));
        var standings = (await c.QueryAsync<PublicLeaderboardRow>(Q.Cmd(Sql.LeaderboardTop5, null, ct))).AsList();
        var sports = (await c.QueryAsync<PublicSportDto>(Q.Cmd(Sql.PublicSports, null, ct))).AsList();
        return new HomeDto(DateTime.UtcNow, stats, live, today, latest, next, standings, sports);
    }

    public async Task<PagedResult<FeedFixtureDto>> ListFeedAsync(FeedQuery q, CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        var p = new { status = q.Status, tournament = q.TournamentId, sport = q.SportId, day = q.DayUtc?.Date, size = q.PageSize, offset = (q.Page - 1) * q.PageSize };
        var total = await c.ExecuteScalarAsync<int>(Q.Cmd(Sql.FeedCount, p, ct));
        return new((await c.QueryAsync<FeedFixtureDto>(Q.Cmd(Sql.FeedPage, p, ct))).AsList(), q.Page, q.PageSize, total);
    }

    public async Task<IReadOnlyList<StandingRow>> GetStandingsAsync(Guid tournamentId, CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        return (await c.QueryAsync<StandingRow>(Q.Cmd(Sql.Standings, new { id = tournamentId }, ct))).AsList();
    }
}
