// Mirrors backend/Contracts/Dtos.cs (camelCase JSON). Keep in sync with docs/API.md.
export type Role = 'Captain' | 'Scorer' | 'Stakeholder' | 'Admin';
export type TeamStatus = 'Pending' | 'Approved' | 'Rejected' | 'Withdrawn';
export type FixtureStatus = 'Pending' | 'Ready' | 'Scheduled' | 'Live' | 'Completed' | 'Walkover' | 'DoubleForfeit' | 'Abandoned' | 'Bye';
export type TournamentStatus = 'Draft' | 'RegistrationOpen' | 'RegistrationClosed' | 'InProgress' | 'Completed' | 'Cancelled';

export interface Paged<T> { items: T[]; page: number; pageSize: number; totalCount: number; totalPages: number }
export interface CurrentUser { id: string; email: string; displayName: string; role: Role; emailVerified: boolean }

export interface PublicSport { id: string; name: string; slug: string; description: string | null }
export interface PublicTournament {
  id: string; name: string; sportId: string; sportName: string; status: TournamentStatus; registrationOpen: boolean;
  startsAtUtc: string | null; endsAtUtc: string | null; timezone: string; approvedTeams: number; teamLimit: number | null;
  fixturesTotal: number; fixturesCompleted: number; minPlayers: number; maxPlayers: number;
}
export interface PublicTeam { id: string; name: string; department: string; sportName: string; tournamentName: string }
export interface LeaderboardRow { department: string; points: number; matchesWon: number; teamsApproved: number }
export interface StandingRow { team: string; department: string; played: number; wins: number; losses: number }

export interface FeedFixture {
  id: string; tournamentId: string; tournamentName: string; sportName: string; round: number; slot: number; isThirdPlace: boolean;
  status: FixtureStatus; homeTeam: string | null; homeDepartment: string | null; awayTeam: string | null; awayDepartment: string | null;
  homeScore: number | null; awayScore: number | null; winnerTeam: string | null; venue: string | null; startsAtUtc: string | null;
  livePeriod: string | null; liveClock: string | null;
}
export interface Home {
  generatedAtUtc: string;
  stats: { sportsActive: number; teamsApproved: number; matchesPlayed: number; departments: number; liveNow: number };
  live: FeedFixture[]; today: FeedFixture[]; latest: FeedFixture[]; nextUp: FeedFixture | null;
  standings: LeaderboardRow[]; sports: PublicSport[];
}

export interface PlayerInput { fullName: string; enrollmentNo: string; department: string }
export interface RegisterTeamRequest { teamName: string; department: string; players: PlayerInput[] }
export interface CaptainTeam {
  teamId: string; teamName: string; department: string; status: TeamStatus; rejectionReason: string | null;
  tournamentId: string; tournamentName: string; sportName: string; rosterCount: number; submittedAtUtc: string;
}
export interface Notification { id: string; type: string; title: string; body: string | null; entityType: string | null; entityId: string | null; readAtUtc: string | null; createdAtUtc: string }

// ---- operations (Scorer)
export interface OpsTournament { id: string; name: string; sportName: string; status: TournamentStatus; teamLimit: number | null; approvedTeams: number; pendingTeams: number; fixturesTotal: number; fixturesResolved: number }
export interface OpsFixture {
  id: string; round: number; slot: number; isThirdPlace: boolean; status: FixtureStatus; homeTeamId: string | null; homeTeam: string | null;
  awayTeamId: string | null; awayTeam: string | null; homeScore: number | null; awayScore: number | null; winnerTeamId: string | null;
  venue: string | null; startsAtUtc: string | null; endsAtUtc: string | null; version: number;
}
export interface RegenerationPreview { included: string[]; excluded: string[]; newFixtures: number; canConfirm: boolean; reason: string | null }
export interface WithdrawResult { walkoversApplied: number; manualWalkoversNeeded: number }

// ---- admin (read-only)
export interface UserSummary { id: string; displayName: string; email: string; role: Role; emailVerified: boolean; isActive: boolean; teamsOwned: number; lastSignInAtUtc: string | null; createdAtUtc: string }
export interface OwnedTeam { teamId: string; teamName: string; status: TeamStatus; tournamentName: string; sportName: string }
export interface AuditEvent { id: string; occurredAtUtc: string; actorId: string | null; actorName: string | null; actorRole: string | null; action: string; entityType: string | null; entityId: string | null; correlationId: string | null; result: string; metadataJson: string | null }
export interface UserDetail extends Omit<UserSummary, 'teamsOwned'> { roleSource: string; googleSubject: string; teams: OwnedTeam[]; recentActivity: AuditEvent[] }
export interface Participant { playerId: string; fullName: string; enrollmentNo: string; department: string; teamId: string; teamName: string; teamStatus: TeamStatus; sportName: string; tournamentName: string; tournamentId: string; captainName: string; registeredAtUtc: string }
export interface TeamSummary { teamId: string; teamName: string; department: string; status: TeamStatus; sportName: string; tournamentName: string; tournamentId: string; captainName: string; captainEmail: string; rosterCount: number; submittedAtUtc: string; rejectionReason: string | null; version: number }
export interface TeamDetail { team: TeamSummary; roster: { playerId: string; fullName: string; enrollmentNo: string; department: string }[] }
export interface NamedCount { name: string; count: number }
export interface Dashboard {
  generatedAtUtc: string; totalUsers: number; activeCaptains: number; totalTeams: number; totalParticipants: number; totalTournaments: number;
  pendingTeams: number; approvedTeams: number; rejectedTeams: number; withdrawnTeams: number; stalePendingTeams: number; emailFailures: number;
  usersByRole: NamedCount[]; participantsByDepartment: NamedCount[]; registrationsPerDay: { dayUtc: string; count: number }[];
}
export interface DepartmentStat { department: string; participants: number; teams: number; approvedTeams: number }
