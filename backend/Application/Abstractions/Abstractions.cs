using SportsWeek.Contracts;
using SportsWeek.Domain.Enums;

namespace SportsWeek.Application.Abstractions;

public interface IClock { DateTime UtcNow { get; } }

/// <summary>The authenticated caller, built server-side from the DB profile — never from client-supplied role claims.</summary>
public sealed record Actor(Guid UserId, string Email, UserRole Role, bool EmailVerified, string? IpAddress, string? CorrelationId);

public sealed record AuditEntry(
    Guid? ActorId, string Action, string? EntityType, string? EntityId, string Result,
    string? CorrelationId, string? IpAddress, string? MetadataJson = null);

/// <summary>Append-only. There is intentionally no update or delete.</summary>
public interface IAuditWriter { Task WriteAsync(AuditEntry entry, CancellationToken ct = default); }

public interface IAdminReadStore
{
    Task<PagedResult<UserSummaryDto>> ListUsersAsync(UserQuery q, CancellationToken ct);
    Task<UserDetailDto?> GetUserAsync(Guid id, CancellationToken ct);
    Task<PagedResult<ParticipantDto>> ListParticipantsAsync(ParticipantQuery q, CancellationToken ct);
    Task<PagedResult<TeamSummaryDto>> ListTeamsAsync(TeamQuery q, CancellationToken ct);
    Task<TeamDetailDto?> GetTeamAsync(Guid id, CancellationToken ct);
    Task<DashboardDto> GetDashboardAsync(CancellationToken ct);
    Task<IReadOnlyList<DepartmentStatDto>> GetDepartmentStatsAsync(CancellationToken ct);
    Task<PagedResult<AuditEventDto>> ListAuditAsync(AuditQuery q, CancellationToken ct);
}

public sealed record TournamentRules(
    Guid Id, string Status, bool RegistrationOpen, int? TeamLimit, int MinPlayers, int MaxPlayers, int ActiveTeamCount);

public sealed record CreateTeamCommand(
    Guid OwnerId, Guid TournamentId, string TeamName, string Department,
    IReadOnlyList<PlayerInput> Players, string IdempotencyKey);

public enum CreateTeamOutcome { Created, IdempotentReplay, DuplicateTeamName, DuplicatePlayers, LimitReached }

public sealed record CreateTeamResult(CreateTeamOutcome Outcome, Guid? TeamId, IReadOnlyList<string>? ConflictingEnrollments = null);

public interface ICaptainStore
{
    Task<TournamentRules?> GetTournamentRulesAsync(Guid tournamentId, CancellationToken ct);
    /// <summary>Transactional insert. Must resolve races via unique constraints, not check-then-insert.</summary>
    Task<CreateTeamResult> CreateTeamAsync(CreateTeamCommand cmd, CancellationToken ct);
    Task<IReadOnlyList<CaptainTeamDto>> ListOwnedTeamsAsync(Guid ownerId, CancellationToken ct);
}

public sealed record ProfileUpsert(
    Guid Id, string? GoogleSubject, string Email, string EmailNormalized, string DisplayName, string? AvatarUrl,
    bool EmailVerified, UserRole Role, string RoleSource);

public sealed record ProfileSnapshot(
    Guid Id, string Email, string DisplayName, UserRole Role, bool EmailVerified, bool IsActive, bool IsNew, UserRole? PreviousRole);

public enum UpsertOutcome { Ok, EmailBelongsToAnotherAccount }

public sealed record UpsertResult(UpsertOutcome Outcome, ProfileSnapshot? Profile);

public interface IProfileStore { Task<UpsertResult> UpsertAsync(ProfileUpsert p, CancellationToken ct); }

public interface IPublicReadStore
{
    Task<IReadOnlyList<PublicSportDto>> ListSportsAsync(CancellationToken ct);
    Task<PagedResult<PublicTournamentDto>> ListTournamentsAsync(int page, int pageSize, string? status, CancellationToken ct);
    Task<PublicTournamentDto?> GetTournamentAsync(Guid id, CancellationToken ct);
    Task<PagedResult<PublicFixtureDto>> ListFixturesAsync(Guid tournamentId, int page, int pageSize, CancellationToken ct);
    Task<PagedResult<PublicTeamDto>> ListTeamsAsync(string? search, int page, int pageSize, CancellationToken ct);
    Task<IReadOnlyList<PublicLeaderboardRow>> GetOverallLeaderboardAsync(CancellationToken ct);
    Task<HomeDto> GetHomeAsync(CancellationToken ct);
    Task<PagedResult<FeedFixtureDto>> ListFeedAsync(FeedQuery q, CancellationToken ct);
    Task<IReadOnlyList<StandingRow>> GetStandingsAsync(Guid tournamentId, CancellationToken ct);
}

// ================= operations store =================
public sealed record TeamOpsState(Guid Id, Guid OwnerId, string Name, string Status, int Version, Guid TournamentId, string TournamentStatus);
public sealed record TournamentOpsState(Guid Id, string Name, string Status, bool ThirdPlace, int MatchDurationMin);
public sealed record TeamRef(Guid Id, Guid OwnerId, string Name, string Status, DateTime CreatedAtUtc);
public sealed record StoredFixture(SportsWeek.Domain.Engine.FixtureNode Node, int Version, string? Venue, DateTime? StartsAtUtc, DateTime? EndsAtUtc);

public interface IOpsStore
{
    Task<TeamOpsState?> GetTeamAsync(Guid id, CancellationToken ct);
    /// <param name="expectedVersion">-1 skips the optimistic check (system-initiated changes such as withdrawal).</param>
    Task<bool> SetTeamStatusAsync(Guid id, string status, string? reason, int expectedVersion, CancellationToken ct);
    Task<TournamentOpsState?> GetTournamentAsync(Guid id, CancellationToken ct);
    Task<Guid?> CreateTournamentAsync(CreateTournamentRequest r, CancellationToken ct);
    Task SetTournamentStatusAsync(Guid id, string status, CancellationToken ct);
    Task<IReadOnlyList<TeamRef>> ListTeamsAsync(Guid tournamentId, CancellationToken ct);
    Task<IReadOnlyList<OpsTournamentDto>> ListTournamentsAsync(CancellationToken ct);
    Task<Guid?> GetFixtureTournamentAsync(Guid fixtureId, CancellationToken ct);
    Task<IReadOnlyList<StoredFixture>> ListFixturesAsync(Guid tournamentId, CancellationToken ct);
    Task InsertFixturesAsync(Guid tournamentId, IReadOnlyList<SportsWeek.Domain.Engine.FixtureNode> nodes, CancellationToken ct);
    Task DeleteUnresolvedFixturesAsync(Guid tournamentId, CancellationToken ct);
    /// <summary>Atomic all-or-nothing save; false when any expected version no longer matches.</summary>
    Task<bool> SaveFixturesAsync(IReadOnlyList<(SportsWeek.Domain.Engine.FixtureNode Node, int ExpectedVersion)> changes, CancellationToken ct);
    Task<bool> SaveScheduleAsync(Guid fixtureId, string venue, DateTime startUtc, DateTime endUtc, int expectedVersion, CancellationToken ct);
    Task SetLiveDetailAsync(Guid fixtureId, string? period, string? clock, CancellationToken ct);
    Task<IReadOnlyList<SportsWeek.Domain.Engine.ScheduledSlot>> ListSlotsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct);
    Task AddNotificationAsync(Guid userId, string type, string title, string? body, string? entityType, string? entityId, CancellationToken ct);
}

public interface ICaptainExtrasStore
{
    Task<IReadOnlyList<NotificationDto>> ListNotificationsAsync(Guid userId, int limit, CancellationToken ct);
    Task<int> MarkAllReadAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyList<FeedFixtureDto>> ListFixturesForOwnerAsync(Guid ownerId, CancellationToken ct);
}
