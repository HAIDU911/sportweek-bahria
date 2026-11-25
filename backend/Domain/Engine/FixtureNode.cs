using SportsWeek.Domain.Enums;

namespace SportsWeek.Domain.Engine;

/// <summary>Pure, persistence-free fixture used by the bracket engine.</summary>
public sealed class FixtureNode
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public int Round { get; init; }
    public int Slot { get; init; }
    public bool IsThirdPlace { get; init; }

    public Guid? HomeTeamId { get; set; }
    public Guid? AwayTeamId { get; set; }
    public FixtureStatus Status { get; set; } = FixtureStatus.Pending;
    public Guid? WinnerTeamId { get; set; }
    public int? HomeScore { get; set; }
    public int? AwayScore { get; set; }

    /// <summary>Where the winner is fed.</summary>
    public int? NextRound { get; init; }
    public int? NextSlot { get; init; }
    public bool NextIsHome { get; init; }

    /// <summary>Where the loser is fed (semi-final → third place).</summary>
    public int? LoserNextRound { get; init; }
    public int? LoserNextSlot { get; init; }
    public bool LoserNextIsHome { get; init; }

    public int ParticipantCount => (HomeTeamId is null ? 0 : 1) + (AwayTeamId is null ? 0 : 1);
}
