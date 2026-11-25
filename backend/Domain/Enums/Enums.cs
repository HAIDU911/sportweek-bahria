namespace SportsWeek.Domain.Enums;

public enum UserRole { Captain = 0, Scorer = 1, Stakeholder = 2, Admin = 3 }

public enum TeamStatus { Pending, Approved, Rejected, Withdrawn }

public enum TournamentStatus { Draft, RegistrationOpen, RegistrationClosed, InProgress, Completed, Cancelled }

public enum FixtureStatus
{
    /// <summary>Waiting for one or both participants to be decided.</summary>
    Pending,
    /// <summary>Both participants known, no time yet.</summary>
    Ready,
    Scheduled,
    Live,
    Completed,
    Walkover,
    DoubleForfeit,
    Abandoned,
    /// <summary>Single participant advances automatically.</summary>
    Bye
}

public enum MatchOutcome { Completed, Walkover, DoubleForfeit, Abandoned }

public static class EnumExtensions
{
    /// <summary>A resolved fixture is history: ordinary score entry/generation must never overwrite it.</summary>
    public static bool IsResolved(this FixtureStatus s) =>
        s is FixtureStatus.Completed or FixtureStatus.Walkover or FixtureStatus.DoubleForfeit
            or FixtureStatus.Abandoned or FixtureStatus.Bye;
}
