using System.Globalization;
using System.Text.Json;
using SportsWeek.Application.Abstractions;
using SportsWeek.Application.Security;
using SportsWeek.Contracts;
using SportsWeek.Domain.Enums;

namespace SportsWeek.Application.Services;

/// <summary>
/// READ-ONLY administrator use cases: view users and participants.
/// PII is masked by default; revealing or exporting sensitive values is explicit, justified and audited.
/// Nothing in this class mutates users, teams or tournaments.
/// </summary>
public sealed class AdminDirectoryService
{
    public const int MaxExportRows = 5_000;
    public const int MinReasonLength = 10;

    private readonly IAdminReadStore _store;
    private readonly IAuditWriter _audit;

    public AdminDirectoryService(IAdminReadStore store, IAuditWriter audit) { _store = store; _audit = audit; }

    private static ServiceResult<T> Deny<T>(Actor a) =>
        a.Role == UserRole.Admin ? default! : ServiceResult<T>.Forbidden("Administrator access required.");

    // ---------------- users ----------------
    public async Task<ServiceResult<PagedResult<UserSummaryDto>>> ListUsersAsync(Actor actor, UserQuery q, CancellationToken ct)
    {
        if (actor.Role != UserRole.Admin) return Deny<PagedResult<UserSummaryDto>>(actor);
        var (page, size) = Paging.Normalize(q.Page, q.PageSize);
        var res = await _store.ListUsersAsync(q with { Page = page, PageSize = size, Search = Clean(q.Search) }, ct);
        return ServiceResult<PagedResult<UserSummaryDto>>.Ok(
            res with { Items = res.Items.Select(u => u with { Email = Masking.Email(u.Email) }).ToList() });
    }

    public async Task<ServiceResult<UserDetailDto>> GetUserAsync(Actor actor, Guid id, bool revealEmail, CancellationToken ct)
    {
        if (actor.Role != UserRole.Admin) return Deny<UserDetailDto>(actor);
        var u = await _store.GetUserAsync(id, ct);
        if (u is null) return ServiceResult<UserDetailDto>.NotFound("User not found.");

        // Viewing a user's detail is itself an auditable access to personal data.
        await _audit.WriteAsync(Entry(actor, revealEmail ? "admin.user.revealed_email" : "admin.user.viewed", "profile", id.ToString(), null), ct);
        return ServiceResult<UserDetailDto>.Ok(revealEmail ? u : u with { Email = Masking.Email(u.Email) });
    }

    // ---------------- participants & teams ----------------
    public async Task<ServiceResult<PagedResult<ParticipantDto>>> ListParticipantsAsync(Actor actor, ParticipantQuery q, CancellationToken ct)
    {
        if (actor.Role != UserRole.Admin) return Deny<PagedResult<ParticipantDto>>(actor);
        var (page, size) = Paging.Normalize(q.Page, q.PageSize);
        var res = await _store.ListParticipantsAsync(q with { Page = page, PageSize = size, Search = Clean(q.Search) }, ct);
        return ServiceResult<PagedResult<ParticipantDto>>.Ok(
            res with { Items = res.Items.Select(p => p with { EnrollmentNo = Masking.Enrollment(p.EnrollmentNo) }).ToList() });
    }

    public async Task<ServiceResult<PagedResult<TeamSummaryDto>>> ListTeamsAsync(Actor actor, TeamQuery q, CancellationToken ct)
    {
        if (actor.Role != UserRole.Admin) return Deny<PagedResult<TeamSummaryDto>>(actor);
        var (page, size) = Paging.Normalize(q.Page, q.PageSize);
        var res = await _store.ListTeamsAsync(q with { Page = page, PageSize = size, Search = Clean(q.Search) }, ct);
        return ServiceResult<PagedResult<TeamSummaryDto>>.Ok(
            res with { Items = res.Items.Select(t => t with { CaptainEmail = Masking.Email(t.CaptainEmail) }).ToList() });
    }

    public async Task<ServiceResult<TeamDetailDto>> GetTeamAsync(Actor actor, Guid id, CancellationToken ct)
    {
        if (actor.Role != UserRole.Admin) return Deny<TeamDetailDto>(actor);
        var t = await _store.GetTeamAsync(id, ct);
        if (t is null) return ServiceResult<TeamDetailDto>.NotFound("Team not found.");
        await _audit.WriteAsync(Entry(actor, "admin.team.viewed", "team", id.ToString(), null), ct);
        return ServiceResult<TeamDetailDto>.Ok(t with
        {
            Team = t.Team with { CaptainEmail = Masking.Email(t.Team.CaptainEmail) },
            Roster = t.Roster.Select(p => p with { EnrollmentNo = Masking.Enrollment(p.EnrollmentNo) }).ToList()
        });
    }

    // ---------------- dashboard / audit ----------------
    public async Task<ServiceResult<DashboardDto>> GetDashboardAsync(Actor actor, CancellationToken ct) =>
        actor.Role != UserRole.Admin ? Deny<DashboardDto>(actor) : ServiceResult<DashboardDto>.Ok(await _store.GetDashboardAsync(ct));

    public async Task<ServiceResult<IReadOnlyList<DepartmentStatDto>>> GetDepartmentsAsync(Actor actor, CancellationToken ct) =>
        actor.Role != UserRole.Admin ? Deny<IReadOnlyList<DepartmentStatDto>>(actor)
            : ServiceResult<IReadOnlyList<DepartmentStatDto>>.Ok(await _store.GetDepartmentStatsAsync(ct));

    public async Task<ServiceResult<PagedResult<AuditEventDto>>> ListAuditAsync(Actor actor, AuditQuery q, CancellationToken ct)
    {
        if (actor.Role != UserRole.Admin) return Deny<PagedResult<AuditEventDto>>(actor);
        var (page, size) = Paging.Normalize(q.Page, q.PageSize);
        return ServiceResult<PagedResult<AuditEventDto>>.Ok(await _store.ListAuditAsync(q with { Page = page, PageSize = size }, ct));
    }

    // ---------------- exports (audited) ----------------
    public async Task<ServiceResult<ExportResult>> ExportUsersAsync(Actor actor, UserQuery q, bool includeSensitive, string? reason, CancellationToken ct)
    {
        if (actor.Role != UserRole.Admin) return Deny<ExportResult>(actor);
        var invalid = ValidateReason(includeSensitive, reason);
        if (invalid is not null) return invalid;

        var res = await _store.ListUsersAsync(q with { Page = 1, PageSize = MaxExportRows, Search = Clean(q.Search) }, ct);
        var csv = CsvBuilder.Build(
            ["id", "display_name", "email", "role", "email_verified", "active", "teams_owned", "last_sign_in_utc", "created_utc"],
            res.Items.Select(u => (IReadOnlyList<string?>)[
                u.Id.ToString(), u.DisplayName, includeSensitive ? u.Email : Masking.Email(u.Email), u.Role,
                u.EmailVerified.ToString(CultureInfo.InvariantCulture), u.IsActive.ToString(CultureInfo.InvariantCulture),
                u.TeamsOwned.ToString(CultureInfo.InvariantCulture), Iso(u.LastSignInAtUtc), Iso(u.CreatedAtUtc)]));
        await _audit.WriteAsync(Entry(actor, "admin.export.users", "export", null, new { rows = res.Items.Count, sensitive = includeSensitive, reason }), ct);
        return ServiceResult<ExportResult>.Ok(new ExportResult($"users-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv", csv, res.Items.Count, res.TotalCount > res.Items.Count));
    }

    public async Task<ServiceResult<ExportResult>> ExportParticipantsAsync(Actor actor, ParticipantQuery q, bool includeSensitive, string? reason, CancellationToken ct)
    {
        if (actor.Role != UserRole.Admin) return Deny<ExportResult>(actor);
        var invalid = ValidateReason(includeSensitive, reason);
        if (invalid is not null) return invalid;

        var res = await _store.ListParticipantsAsync(q with { Page = 1, PageSize = MaxExportRows, Search = Clean(q.Search) }, ct);
        var csv = CsvBuilder.Build(
            ["player_id", "full_name", "enrollment_no", "department", "team", "team_status", "sport", "tournament", "captain", "registered_utc"],
            res.Items.Select(p => (IReadOnlyList<string?>)[
                p.PlayerId.ToString(), p.FullName, includeSensitive ? p.EnrollmentNo : Masking.Enrollment(p.EnrollmentNo),
                p.Department, p.TeamName, p.TeamStatus, p.SportName, p.TournamentName, p.CaptainName, Iso(p.RegisteredAtUtc)]));
        await _audit.WriteAsync(Entry(actor, "admin.export.participants", "export", null, new { rows = res.Items.Count, sensitive = includeSensitive, reason }), ct);
        return ServiceResult<ExportResult>.Ok(new ExportResult($"participants-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv", csv, res.Items.Count, res.TotalCount > res.Items.Count));
    }

    // ---------------- helpers ----------------
    private static ServiceResult<ExportResult>? ValidateReason(bool sensitive, string? reason) =>
        sensitive && (reason?.Trim().Length ?? 0) < MinReasonLength
            ? ServiceResult<ExportResult>.Invalid(new Dictionary<string, string[]>
              { ["reason"] = [$"A reason of at least {MinReasonLength} characters is required for sensitive exports."] })
            : null;

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim()[..Math.Min(s.Trim().Length, 100)];
    private static string Iso(DateTime? d) => d?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? string.Empty;

    private static AuditEntry Entry(Actor a, string action, string? type, string? id, object? meta) =>
        new(a.UserId, action, type, id, "success", a.CorrelationId, a.IpAddress, meta is null ? null : JsonSerializer.Serialize(meta));
}

public sealed record ExportResult(string FileName, string Csv, int RowCount, bool Truncated);
