using Dapper;
using Npgsql;
using SportsWeek.Application.Abstractions;
using SportsWeek.Contracts;

namespace SportsWeek.Infrastructure.Data;

/// <summary>Read-only queries for the administrator console. This class has no INSERT/UPDATE/DELETE against business data.</summary>
public sealed class AdminReadStore : IAdminReadStore
{
    private readonly NpgsqlDataSource _ds;
    public AdminReadStore(NpgsqlDataSource ds) => _ds = ds;

    private sealed record UserRow(Guid Id, string DisplayName, string Email, string Role, string RoleSource, bool EmailVerified,
        bool IsActive, string GoogleSubject, DateTime? LastSignInAtUtc, DateTime CreatedAtUtc);

    private sealed record CountsRow(int TotalUsers, int ActiveCaptains, int TotalTeams, int TotalParticipants, int TotalTournaments,
        int PendingTeams, int ApprovedTeams, int RejectedTeams, int WithdrawnTeams, int StalePendingTeams, int EmailFailures);

    private static int Offset(int page, int size) => (page - 1) * size;

    public async Task<PagedResult<UserSummaryDto>> ListUsersAsync(UserQuery q, CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        var p = new
        {
            search = q.Search, pattern = Q.Pattern(q.Search), role = q.Role, verified = q.Verified,
            from = Q.Utc(q.CreatedFromUtc), to = Q.Utc(q.CreatedToUtc), size = q.PageSize, offset = Offset(q.Page, q.PageSize)
        };
        var total = await c.ExecuteScalarAsync<int>(Q.Cmd(Sql.UsersCount, p, ct));
        var rows = (await c.QueryAsync<UserSummaryDto>(Q.Cmd(Sql.UsersPage, p, ct))).AsList();
        return new PagedResult<UserSummaryDto>(rows, q.Page, q.PageSize, total);
    }

    public async Task<UserDetailDto?> GetUserAsync(Guid id, CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        var u = await c.QuerySingleOrDefaultAsync<UserRow>(Q.Cmd(Sql.UserById, new { id }, ct));
        if (u is null) return null;
        var teams = (await c.QueryAsync<OwnedTeamDto>(Q.Cmd(Sql.UserTeams, new { id }, ct))).AsList();
        var activity = (await c.QueryAsync<AuditEventDto>(Q.Cmd(Sql.AuditForUser, new { id }, ct))).AsList();
        return new UserDetailDto(u.Id, u.DisplayName, u.Email, u.Role, u.RoleSource, u.EmailVerified, u.IsActive,
            u.GoogleSubject, u.LastSignInAtUtc, u.CreatedAtUtc, teams, activity);
    }

    public async Task<PagedResult<ParticipantDto>> ListParticipantsAsync(ParticipantQuery q, CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        var p = new
        {
            search = q.Search, pattern = Q.Pattern(q.Search), tournament = q.TournamentId, sport = q.SportId,
            department = q.Department, status = q.TeamStatus, size = q.PageSize, offset = Offset(q.Page, q.PageSize)
        };
        var total = await c.ExecuteScalarAsync<int>(Q.Cmd(Sql.ParticipantsCount, p, ct));
        var rows = (await c.QueryAsync<ParticipantDto>(Q.Cmd(Sql.ParticipantsPage, p, ct))).AsList();
        return new PagedResult<ParticipantDto>(rows, q.Page, q.PageSize, total);
    }

    public async Task<PagedResult<TeamSummaryDto>> ListTeamsAsync(TeamQuery q, CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        var p = new
        {
            search = q.Search, pattern = Q.Pattern(q.Search), tournament = q.TournamentId, status = q.Status,
            size = q.PageSize, offset = Offset(q.Page, q.PageSize)
        };
        var total = await c.ExecuteScalarAsync<int>(Q.Cmd(Sql.TeamsCount, p, ct));
        var rows = (await c.QueryAsync<TeamSummaryDto>(Q.Cmd(Sql.TeamsPage, p, ct))).AsList();
        return new PagedResult<TeamSummaryDto>(rows, q.Page, q.PageSize, total);
    }

    public async Task<TeamDetailDto?> GetTeamAsync(Guid id, CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        var t = await c.QuerySingleOrDefaultAsync<TeamSummaryDto>(Q.Cmd(Sql.TeamById, new { id }, ct));
        if (t is null) return null;
        var roster = (await c.QueryAsync<TeamPlayerDto>(Q.Cmd(Sql.TeamRoster, new { id }, ct))).AsList();
        return new TeamDetailDto(t, roster);
    }

    public async Task<DashboardDto> GetDashboardAsync(CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        var n = await c.QuerySingleAsync<CountsRow>(Q.Cmd(Sql.DashboardCounts, null, ct));
        var roles = (await c.QueryAsync<NamedCount>(Q.Cmd(Sql.UsersByRole, null, ct))).AsList();
        var depts = (await c.QueryAsync<NamedCount>(Q.Cmd(Sql.ParticipantsByDepartment, null, ct))).AsList();
        var days = (await c.QueryAsync<DailyCount>(Q.Cmd(Sql.RegistrationsPerDay, null, ct))).AsList();
        return new DashboardDto(DateTime.UtcNow, n.TotalUsers, n.ActiveCaptains, n.TotalTeams, n.TotalParticipants, n.TotalTournaments,
            n.PendingTeams, n.ApprovedTeams, n.RejectedTeams, n.WithdrawnTeams, n.StalePendingTeams, n.EmailFailures, roles, depts, days);
    }

    public async Task<IReadOnlyList<DepartmentStatDto>> GetDepartmentStatsAsync(CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        return (await c.QueryAsync<DepartmentStatDto>(Q.Cmd(Sql.DepartmentStats, null, ct))).AsList();
    }

    public async Task<PagedResult<AuditEventDto>> ListAuditAsync(AuditQuery q, CancellationToken ct)
    {
        await using var c = await _ds.OpenConnectionAsync(ct);
        var p = new
        {
            action = q.Action, actor = q.ActorId, entity = q.EntityType, from = Q.Utc(q.FromUtc), to = Q.Utc(q.ToUtc),
            size = q.PageSize, offset = Offset(q.Page, q.PageSize)
        };
        var total = await c.ExecuteScalarAsync<int>(Q.Cmd(Sql.AuditCount, p, ct));
        var rows = (await c.QueryAsync<AuditEventDto>(Q.Cmd(Sql.AuditPage, p, ct))).AsList();
        return new PagedResult<AuditEventDto>(rows, q.Page, q.PageSize, total);
    }
}
