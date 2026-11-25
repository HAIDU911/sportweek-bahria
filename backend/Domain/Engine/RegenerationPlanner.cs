using SportsWeek.Domain.Common;
using SportsWeek.Domain.Enums;

namespace SportsWeek.Domain.Engine;

public sealed record TeamState(Guid Id, int Seed, TeamStatus Status);

/// <summary>A resolved historical match. Winner is null for a double forfeit (both teams out).</summary>
public sealed record PlayedMatch(Guid TeamA, Guid? TeamB, Guid? WinnerId);

public enum ExclusionReason { Withdrawn, Rejected, NotApproved, Eliminated }

public sealed record Exclusion(Guid TeamId, ExclusionReason Reason);

public sealed record RegenerationPlan(
    IReadOnlyList<Guid> IncludedTeams,
    IReadOnlyList<Exclusion> Excluded,
    Bracket? ContinuationBracket);

/// <summary>
/// Continuation regeneration: preserves history, excludes withdrawn / rejected / eliminated teams,
/// includes eligible never-played teams, and never replays a completed matchup.
/// </summary>
public static class RegenerationPlanner
{
    public static RegenerationPlan Plan(
        IReadOnlyList<TeamState> teams,
        IReadOnlyList<PlayedMatch> history,
        int lastRoundPlayed,
        bool includeThirdPlace = false)
    {
        ArgumentNullException.ThrowIfNull(teams);
        ArgumentNullException.ThrowIfNull(history);

        var eliminated = new HashSet<Guid>();
        foreach (var m in history)
        {
            if (m.TeamB is null) continue; // bye: nobody eliminated
            if (m.WinnerId is null) { eliminated.Add(m.TeamA); eliminated.Add(m.TeamB.Value); }
            else eliminated.Add(m.WinnerId == m.TeamA ? m.TeamB.Value : m.TeamA);
        }

        var included = new List<TeamState>();
        var excluded = new List<Exclusion>();
        foreach (var t in teams.OrderBy(t => t.Seed).ThenBy(t => t.Id))
        {
            if (t.Status == TeamStatus.Withdrawn) excluded.Add(new(t.Id, ExclusionReason.Withdrawn));
            else if (t.Status == TeamStatus.Rejected) excluded.Add(new(t.Id, ExclusionReason.Rejected));
            else if (t.Status != TeamStatus.Approved) excluded.Add(new(t.Id, ExclusionReason.NotApproved));
            else if (eliminated.Contains(t.Id)) excluded.Add(new(t.Id, ExclusionReason.Eliminated));
            else included.Add(t);
        }

        if (included.Count < BracketGenerator.MinTeams)
            return new RegenerationPlan(included.Select(i => i.Id).ToList(), excluded, null);

        var played = history.Where(h => h.TeamB is not null && h.WinnerId is not null)
            .Select(h => Key(h.TeamA, h.TeamB!.Value)).ToHashSet();

        var ordered = ArrangeAvoidingRematches(included.Select(i => i.Id).ToList(), played);
        var bracket = BracketGenerator.Generate(ordered, includeThirdPlace, roundOffset: lastRoundPlayed);

        // Invariant: no first-round fixture replays a completed matchup.
        foreach (var f in bracket.Fixtures.Where(x => x.Round == lastRoundPlayed + 1 && x.ParticipantCount == 2))
            if (played.Contains(Key(f.HomeTeamId!.Value, f.AwayTeamId!.Value)))
                throw new DomainException("regen.rematch", "Regeneration would replay a completed matchup.");

        return new RegenerationPlan(ordered, excluded, bracket);
    }

    /// <summary>Swaps teams so that no first-round pair is a previously completed matchup, keeping seed order otherwise.</summary>
    public static IReadOnlyList<Guid> ArrangeAvoidingRematches(IReadOnlyList<Guid> seeded, ISet<(Guid, Guid)> playedPairs)
    {
        var list = seeded.ToList();
        var n = list.Count;
        var size = 1; while (size < n) size <<= 1;
        var order = BracketGenerator.SeedOrder(size);

        (int a, int b)[] Pairs() => Enumerable.Range(0, size / 2)
            .Select(i => (order[i * 2], order[i * 2 + 1])).Where(p => p.Item1 <= n && p.Item2 <= n).ToArray();

        for (var pass = 0; pass < n * n; pass++)
        {
            var bad = Pairs().FirstOrDefault(p => playedPairs.Contains(Key(list[p.a - 1], list[p.b - 1])));
            if (bad == default) return list;
            var fixedIt = false;
            for (var i = 1; i <= n && !fixedIt; i++)
            {
                if (i == bad.a || i == bad.b) continue;
                (list[bad.b - 1], list[i - 1]) = (list[i - 1], list[bad.b - 1]);
                if (!Pairs().Any(p => playedPairs.Contains(Key(list[p.a - 1], list[p.b - 1])))) { fixedIt = true; break; }
                (list[bad.b - 1], list[i - 1]) = (list[i - 1], list[bad.b - 1]); // revert
            }
            if (!fixedIt) throw new DomainException("regen.rematch", "Unable to avoid replaying a completed matchup.");
        }
        return list;
    }

    public static (Guid, Guid) Key(Guid a, Guid b) => a.CompareTo(b) <= 0 ? (a, b) : (b, a);
}
