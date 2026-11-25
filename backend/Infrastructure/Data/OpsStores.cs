using Dapper;
using Npgsql;
using SportsWeek.Application.Abstractions;
using SportsWeek.Contracts;
using SportsWeek.Domain.Engine;
using SportsWeek.Domain.Enums;

namespace SportsWeek.Infrastructure.Data;

public sealed class OpsStore : IOpsStore
{
    private readonly NpgsqlDataSource _ds;
    public OpsStore(NpgsqlDataSource ds) => _ds = ds;

    private sealed record FixtureRow(Guid Id, int Round, int Slot, bool IsThirdPlace, string Status, Guid? HomeTeamId, Guid? AwayTeamId,
        Guid? WinnerTeamId, int? HomeScore, int? AwayScore, int? NextRound, int? NextSlot, bool NextIsHome,
        int? LoserNextRound, int? LoserNextSlot, bool LoserNextIsHome, int Version, string? Venue, DateTime? StartsAtUtc, DateTime? EndsAtUtc);

    private sealed record SlotRow(Guid Id, Guid? HomeTeamId, Guid? AwayTeamId, string? Venue, DateTime StartUtc, DateTime EndUtc);

    public async Task<TeamOpsState?> GetTeamAsync(Guid id, CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        return await c.QuerySingleOrDefaultAsync<TeamOpsState>(Q.Cmd(Sql.OpsTeam, new { id }, ct));
    }

    public async Task<bool> SetTeamStatusAsync(Guid id, string status, string? reason, int expectedVersion, CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        return await c.ExecuteAsync(Q.Cmd(Sql.OpsTeamSetStatus, new { id, status, reason, expected = expectedVersion }, ct)) == 1;
    }

    public async Task<TournamentOpsState?> GetTournamentAsync(Guid id, CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        return await c.QuerySingleOrDefaultAsync<TournamentOpsState>(Q.Cmd(Sql.OpsTournament, new { id }, ct));
    }

    public async Task<Guid?> CreateTournamentAsync(CreateTournamentRequest r, CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        return await c.ExecuteScalarAsync<Guid?>(Q.Cmd(Sql.OpsTournamentCreate, new
        { sport = r.SportId, name = r.Name, starts = Q.Utc(r.StartsAtUtc), ends = Q.Utc(r.EndsAtUtc), limit = r.TeamLimit, third = r.ThirdPlaceMatch }, ct));
    }

    public async Task SetTournamentStatusAsync(Guid id, string status, CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        await c.ExecuteAsync(Q.Cmd(Sql.OpsTournamentStatus, new { id, status }, ct));
    }

    public async Task<IReadOnlyList<TeamRef>> ListTeamsAsync(Guid tournamentId, CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        return (await c.QueryAsync<TeamRef>(Q.Cmd(Sql.OpsTeams, new { id = tournamentId }, ct))).AsList();
    }

    public async Task<IReadOnlyList<OpsTournamentDto>> ListTournamentsAsync(CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        return (await c.QueryAsync<OpsTournamentDto>(Q.Cmd(Sql.OpsTournaments, null, ct))).AsList();
    }

    public async Task<Guid?> GetFixtureTournamentAsync(Guid fixtureId, CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        return await c.ExecuteScalarAsync<Guid?>(Q.Cmd(Sql.OpsFixtureTournament, new { id = fixtureId }, ct));
    }

    public async Task<IReadOnlyList<StoredFixture>> ListFixturesAsync(Guid tournamentId, CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        var rows = await c.QueryAsync<FixtureRow>(Q.Cmd(Sql.OpsFixtures, new { id = tournamentId }, ct));
        return rows.Select(r => new StoredFixture(new FixtureNode
        {
            Id = r.Id, Round = r.Round, Slot = r.Slot, IsThirdPlace = r.IsThirdPlace,
            HomeTeamId = r.HomeTeamId, AwayTeamId = r.AwayTeamId, WinnerTeamId = r.WinnerTeamId, HomeScore = r.HomeScore, AwayScore = r.AwayScore,
            Status = Enum.Parse<FixtureStatus>(r.Status),
            NextRound = r.NextRound, NextSlot = r.NextSlot, NextIsHome = r.NextIsHome,
            LoserNextRound = r.LoserNextRound, LoserNextSlot = r.LoserNextSlot, LoserNextIsHome = r.LoserNextIsHome
        }, r.Version, r.Venue, r.StartsAtUtc, r.EndsAtUtc)).ToList();
    }

    public async Task InsertFixturesAsync(Guid tournamentId, IReadOnlyList<FixtureNode> nodes, CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        foreach (var n in nodes)
            await c.ExecuteAsync(Q.Cmd(Sql.OpsFixtureInsert, new
            {
                id = n.Id, tournament = tournamentId, round = n.Round, slot = n.Slot, third = n.IsThirdPlace, home = n.HomeTeamId, away = n.AwayTeamId,
                status = n.Status.ToString(), winner = n.WinnerTeamId, hs = n.HomeScore, @as = n.AwayScore,
                nr = n.NextRound, ns = n.NextSlot, nh = n.NextIsHome, lr = n.LoserNextRound, ls = n.LoserNextSlot, lh = n.LoserNextIsHome
            }, ct, tx));
        await tx.CommitAsync(ct);
    }

    public async Task DeleteUnresolvedFixturesAsync(Guid tournamentId, CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        await c.ExecuteAsync(Q.Cmd(Sql.OpsFixtureDeleteUnresolved, new { id = tournamentId }, ct));
    }

    public async Task<bool> SaveFixturesAsync(IReadOnlyList<(FixtureNode Node, int ExpectedVersion)> changes, CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        foreach (var (n, v) in changes)
        {
            var rows = await c.ExecuteAsync(Q.Cmd(Sql.OpsFixtureSave, new
            {
                id = n.Id, home = n.HomeTeamId, away = n.AwayTeamId, status = n.Status.ToString(), winner = n.WinnerTeamId,
                hs = n.HomeScore, @as = n.AwayScore, expected = v
            }, ct, tx));
            if (rows != 1) { await tx.RollbackAsync(ct); return false; } // optimistic concurrency: all-or-nothing
        }
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<bool> SaveScheduleAsync(Guid fixtureId, string venue, DateTime startUtc, DateTime endUtc, int expectedVersion, CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        return await c.ExecuteAsync(Q.Cmd(Sql.OpsSchedule, new
        { id = fixtureId, venue, start = Q.Utc(startUtc), end = Q.Utc(endUtc), expected = expectedVersion }, ct)) == 1;
    }

    public async Task SetLiveDetailAsync(Guid fixtureId, string? period, string? clock, CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        await c.ExecuteAsync(Q.Cmd(Sql.OpsLiveDetail, new { id = fixtureId, period, clock }, ct));
    }

    public async Task<IReadOnlyList<ScheduledSlot>> ListSlotsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        var rows = await c.QueryAsync<SlotRow>(Q.Cmd(Sql.OpsSlots, new { from = Q.Utc(fromUtc), to = Q.Utc(toUtc) }, ct));
        return rows.Select(r => new ScheduledSlot(r.Id, r.HomeTeamId, r.AwayTeamId, r.Venue, r.StartUtc, r.EndUtc)).ToList();
    }

    public async Task AddNotificationAsync(Guid userId, string type, string title, string? body, string? entityType, string? entityId, CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        await c.ExecuteAsync(Q.Cmd(Sql.NotificationInsert, new { user = userId, type, title, body, entityType, entityId }, ct));
    }
}

public sealed class CaptainExtrasStore : ICaptainExtrasStore
{
    private readonly NpgsqlDataSource _ds;
    public CaptainExtrasStore(NpgsqlDataSource ds) => _ds = ds;

    public async Task<IReadOnlyList<NotificationDto>> ListNotificationsAsync(Guid userId, int limit, CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        return (await c.QueryAsync<NotificationDto>(Q.Cmd(Sql.Notifications, new { user = userId, limit }, ct))).AsList();
    }

    public async Task<int> MarkAllReadAsync(Guid userId, CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        return await c.ExecuteAsync(Q.Cmd(Sql.NotificationsMarkRead, new { user = userId }, ct));
    }

    public async Task<IReadOnlyList<FeedFixtureDto>> ListFixturesForOwnerAsync(Guid ownerId, CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        return (await c.QueryAsync<FeedFixtureDto>(Q.Cmd(Sql.OwnerFixtures, new { owner = ownerId }, ct))).AsList();
    }
}
