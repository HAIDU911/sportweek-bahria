namespace SportsWeek.Contracts;

// ---------- shared ----------
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record ErrorResponse(string Code, string Message, IReadOnlyDictionary<string, string[]>? FieldErrors, string? CorrelationId);

// ---------- admin: users ----------
public sealed record UserQuery(string? Search, string? Role, bool? Verified, DateTime? CreatedFromUtc, DateTime? CreatedToUtc, int Page = 1, int PageSize = 25);

public sealed record UserSummaryDto(
    Guid Id, string DisplayName, string Email, string Role, bool EmailVerified, bool IsActive,
    int TeamsOwned, DateTime? LastSignInAtUtc, DateTime CreatedAtUtc);

public sealed record OwnedTeamDto(Guid TeamId, string TeamName, string Status, string TournamentName, string SportName);

public sealed record UserDetailDto(
    Guid Id, string DisplayName, string Email, string Role, string RoleSource, bool EmailVerified, bool IsActive,
    string GoogleSubject, DateTime? LastSignInAtUtc, DateTime CreatedAtUtc,
    IReadOnlyList<OwnedTeamDto> Teams, IReadOnlyList<AuditEventDto> RecentActivity);

// ---------- admin: participants / teams ----------
public sealed record ParticipantQuery(
    string? Search, Guid? TournamentId, Guid? SportId, string? Department, string? TeamStatus, int Page = 1, int PageSize = 25);

public sealed record ParticipantDto(
    Guid PlayerId, string FullName, string EnrollmentNo, string Department, Guid TeamId, string TeamName,
    string TeamStatus, string SportName, string TournamentName, Guid TournamentId, string CaptainName, DateTime RegisteredAtUtc);

public sealed record TeamQuery(string? Search, Guid? TournamentId, string? Status, int Page = 1, int PageSize = 25);

public sealed record TeamSummaryDto(
    Guid TeamId, string TeamName, string Department, string Status, string SportName, string TournamentName,
    Guid TournamentId, string CaptainName, string CaptainEmail, int RosterCount, DateTime SubmittedAtUtc, string? RejectionReason, int Version);

public sealed record TeamPlayerDto(Guid PlayerId, string FullName, string EnrollmentNo, string Department);

public sealed record TeamDetailDto(TeamSummaryDto Team, IReadOnlyList<TeamPlayerDto> Roster);

// ---------- admin: dashboard / analytics ----------
public sealed record NamedCount(string Name, int Count);
public sealed record DailyCount(DateTime DayUtc, int Count);

public sealed record DashboardDto(
    DateTime GeneratedAtUtc,
    int TotalUsers, int ActiveCaptains, int TotalTeams, int TotalParticipants, int TotalTournaments,
    int PendingTeams, int ApprovedTeams, int RejectedTeams, int WithdrawnTeams,
    int StalePendingTeams, int EmailFailures,
    IReadOnlyList<NamedCount> UsersByRole,
    IReadOnlyList<NamedCount> ParticipantsByDepartment,
    IReadOnlyList<DailyCount> RegistrationsPerDay);

public sealed record DepartmentStatDto(string Department, int Participants, int Teams, int ApprovedTeams);

// ---------- audit ----------
public sealed record AuditQuery(string? Action, Guid? ActorId, string? EntityType, DateTime? FromUtc, DateTime? ToUtc, int Page = 1, int PageSize = 50);

public sealed record AuditEventDto(
    Guid Id, DateTime OccurredAtUtc, Guid? ActorId, string? ActorName, string? ActorRole, string Action,
    string? EntityType, string? EntityId, string? CorrelationId, string Result, string? MetadataJson);

// ---------- captain / public ----------
public sealed record PlayerInput(string FullName, string EnrollmentNo, string Department);

public sealed record RegisterTeamRequest(string TeamName, string Department, IReadOnlyList<PlayerInput> Players);

public sealed record CaptainTeamDto(
    Guid TeamId, string TeamName, string Department, string Status, string? RejectionReason,
    Guid TournamentId, string TournamentName, string SportName, int RosterCount, DateTime SubmittedAtUtc);

public sealed record PublicSportDto(Guid Id, string Name, string Slug, string? Description);

public sealed record PublicTournamentDto(
    Guid Id, string Name, Guid SportId, string SportName, string Status, bool RegistrationOpen,
    DateTime? StartsAtUtc, DateTime? EndsAtUtc, string Timezone, int ApprovedTeams, int? TeamLimit,
    int FixturesTotal, int FixturesCompleted, int MinPlayers, int MaxPlayers);

public sealed record PublicTeamDto(Guid Id, string Name, string Department, string SportName, string TournamentName);

public sealed record PublicFixtureDto(
    Guid Id, int Round, int Slot, bool IsThirdPlace, string Status, string? HomeTeam, string? AwayTeam,
    int? HomeScore, int? AwayScore, string? WinnerTeam, string? Venue, DateTime? StartsAtUtc);

public sealed record PublicLeaderboardRow(string Department, int Points, int MatchesWon, int TeamsApproved);

public sealed record CurrentUserDto(Guid Id, string Email, string DisplayName, string Role, bool EmailVerified);

// ---------- public home / feed ----------
public sealed record FeedFixtureDto(
    Guid Id, Guid TournamentId, string TournamentName, string SportName, int Round, int Slot, bool IsThirdPlace, string Status,
    string? HomeTeam, string? HomeDepartment, string? AwayTeam, string? AwayDepartment, int? HomeScore, int? AwayScore,
    string? WinnerTeam, string? Venue, DateTime? StartsAtUtc, string? LivePeriod, string? LiveClock);

public sealed record PublicStatsDto(int SportsActive, int TeamsApproved, int MatchesPlayed, int Departments, int LiveNow);

public sealed record HomeDto(
    DateTime GeneratedAtUtc, PublicStatsDto Stats, IReadOnlyList<FeedFixtureDto> Live, IReadOnlyList<FeedFixtureDto> Today,
    IReadOnlyList<FeedFixtureDto> Latest, FeedFixtureDto? NextUp, IReadOnlyList<PublicLeaderboardRow> Standings,
    IReadOnlyList<PublicSportDto> Sports);

public sealed record FeedQuery(string? Status, Guid? SportId, Guid? TournamentId, DateTime? DayUtc, int Page = 1, int PageSize = 50);

public sealed record StandingRow(string Team, string Department, int Played, int Wins, int Losses);

// ---------- captain extras ----------
public sealed record NotificationDto(Guid Id, string Type, string Title, string? Body, string? EntityType, string? EntityId, DateTime? ReadAtUtc, DateTime CreatedAtUtc);

// ---------- operations (Scorer) ----------
public sealed record ReviewTeamRequest(string Decision, string? Reason, int Version);
public sealed record ResultRequest(string Outcome, int? HomeScore, int? AwayScore, Guid? WalkoverWinnerId, int Version);
public sealed record ScheduleRequest(string Venue, DateTime StartsAtUtc, DateTime EndsAtUtc, int Version);
public sealed record StartRequest(int Version);
public sealed record LiveDetailRequest(string? Period, string? Clock);
public sealed record WithdrawRequest(string? Reason);
public sealed record CreateTournamentRequest(Guid SportId, string Name, DateTime? StartsAtUtc, DateTime? EndsAtUtc, int? TeamLimit, bool ThirdPlaceMatch);
public sealed record TournamentStatusRequest(string Status);

public sealed record OpsFixtureDto(
    Guid Id, int Round, int Slot, bool IsThirdPlace, string Status, Guid? HomeTeamId, string? HomeTeam, Guid? AwayTeamId, string? AwayTeam,
    int? HomeScore, int? AwayScore, Guid? WinnerTeamId, string? Venue, DateTime? StartsAtUtc, DateTime? EndsAtUtc, int Version);

public sealed record OpsTournamentDto(Guid Id, string Name, string SportName, string Status, int? TeamLimit, int ApprovedTeams, int PendingTeams, int FixturesTotal, int FixturesResolved);

public sealed record RegenerationPreviewDto(IReadOnlyList<string> Included, IReadOnlyList<string> Excluded, int NewFixtures, bool CanConfirm, string? Reason);
public sealed record WithdrawResultDto(int WalkoversApplied, int ManualWalkoversNeeded);
