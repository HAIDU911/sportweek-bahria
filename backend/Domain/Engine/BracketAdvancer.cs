using SportsWeek.Domain.Common;
using SportsWeek.Domain.Enums;

namespace SportsWeek.Domain.Engine;

/// <summary>Applies results to a bracket: winner advancement, byes, bye chains, forfeits.</summary>
public static class BracketAdvancer
{
    /// <summary>Apply a validated result. Throws if the fixture is resolved or lacks participants (no phantom results).</summary>
    public static void ApplyResult(IReadOnlyList<FixtureNode> fixtures, FixtureNode fixture, MatchResult result)
    {
        ArgumentNullException.ThrowIfNull(fixtures);
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(result);
        if (fixture.Status.IsResolved())
            throw new DomainException("fixture.resolved", "A resolved fixture cannot be overwritten by ordinary scoring.");
        if (fixture.ParticipantCount != 2)
            throw new DomainException("fixture.missing_participants", "A fixture with missing participants cannot be completed.");
        if (result.WinnerId is { } w && w != fixture.HomeTeamId && w != fixture.AwayTeamId)
            throw new DomainException("fixture.winner_not_participant", "The winner must be one of the participants.");

        fixture.HomeScore = result.HomeScore;
        fixture.AwayScore = result.AwayScore;
        fixture.WinnerTeamId = result.WinnerId;
        fixture.Status = result.Outcome switch
        {
            MatchOutcome.Completed => FixtureStatus.Completed,
            MatchOutcome.Walkover => FixtureStatus.Walkover,
            MatchOutcome.DoubleForfeit => FixtureStatus.DoubleForfeit,
            _ => FixtureStatus.Abandoned
        };

        Feed(fixtures, fixture, result.WinnerId, result.LoserId);
        Settle(fixtures);
    }

    /// <summary>
    /// Resolve byes and bye chains: a fixture whose feeders are all resolved and which has one participant becomes a Bye;
    /// with none, it collapses (DoubleForfeit, or Abandoned for third place). Also marks Ready/Pending correctly.
    /// </summary>
    public static void Settle(IReadOnlyList<FixtureNode> fixtures)
    {
        var changed = true;
        var guard = 0;
        while (changed)
        {
            if (++guard > 10_000) throw new DomainException("bracket.settle_loop", "Bracket failed to settle.");
            changed = false;
            foreach (var f in fixtures.Where(x => !x.Status.IsResolved()))
            {
                var feedersResolved = FeedersOf(fixtures, f).All(x => x.Status.IsResolved());
                if (feedersResolved && f.ParticipantCount < 2)
                {
                    if (f.ParticipantCount == 1 && !f.IsThirdPlace)
                    {
                        f.Status = FixtureStatus.Bye;
                        f.WinnerTeamId = f.HomeTeamId ?? f.AwayTeamId;
                        Feed(fixtures, f, f.WinnerTeamId, null);
                    }
                    else
                    {
                        f.Status = f.IsThirdPlace ? FixtureStatus.Abandoned : FixtureStatus.DoubleForfeit;
                    }
                    changed = true;
                }
                else
                {
                    var target = f.ParticipantCount == 2 ? FixtureStatus.Ready : FixtureStatus.Pending;
                    // Never downgrade an already scheduled/live fixture.
                    if (f.Status is FixtureStatus.Pending or FixtureStatus.Ready && f.Status != target)
                    {
                        f.Status = target;
                        changed = true;
                    }
                }
            }
        }
    }

    private static IEnumerable<FixtureNode> FeedersOf(IReadOnlyList<FixtureNode> all, FixtureNode f) =>
        all.Where(x => (x.NextRound == f.Round && x.NextSlot == f.Slot && !f.IsThirdPlace)
                    || (x.LoserNextRound == f.Round && x.LoserNextSlot == f.Slot && f.IsThirdPlace));

    private static void Feed(IReadOnlyList<FixtureNode> all, FixtureNode from, Guid? winner, Guid? loser)
    {
        if (winner is not null && from.NextRound is { } nr && from.NextSlot is { } ns)
        {
            var target = all.First(x => x.Round == nr && x.Slot == ns && !x.IsThirdPlace);
            Place(target, from.NextIsHome, winner.Value);
        }
        if (loser is not null && from.LoserNextRound is { } lr && from.LoserNextSlot is { } ls)
        {
            var target = all.First(x => x.Round == lr && x.Slot == ls && x.IsThirdPlace);
            Place(target, from.LoserNextIsHome, loser.Value);
        }
    }

    private static void Place(FixtureNode target, bool home, Guid team)
    {
        if (target.Status.IsResolved())
            throw new DomainException("fixture.downstream_resolved", "Downstream fixture is already resolved; correction is unsafe.");
        if (home) target.HomeTeamId = team; else target.AwayTeamId = team;
    }
}
