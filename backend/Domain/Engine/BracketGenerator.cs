using SportsWeek.Domain.Common;
using SportsWeek.Domain.Enums;

namespace SportsWeek.Domain.Engine;

public sealed record Bracket(IReadOnlyList<FixtureNode> Fixtures, int Size, int Rounds);

/// <summary>Deterministic single-elimination bracket for 2–64 teams with standard seeding and byes.</summary>
public static class BracketGenerator
{
    public const int MinTeams = 2;
    public const int MaxTeams = 64;

    /// <param name="seededTeams">Team ids ordered best seed first.</param>
    /// <param name="includeThirdPlace">Adds a third-place fixture when there are at least 4 slots.</param>
    /// <param name="roundOffset">Added to every round number (used by continuation regeneration).</param>
    public static Bracket Generate(IReadOnlyList<Guid> seededTeams, bool includeThirdPlace = false, int roundOffset = 0)
    {
        ArgumentNullException.ThrowIfNull(seededTeams);
        var n = seededTeams.Count;
        if (n < MinTeams || n > MaxTeams)
            throw new DomainException("bracket.team_count", $"A bracket needs between {MinTeams} and {MaxTeams} teams (got {n}).");
        if (seededTeams.Distinct().Count() != n)
            throw new DomainException("bracket.duplicate_team", "A team cannot appear twice in a bracket.");

        var size = 1;
        while (size < n) size <<= 1;
        var rounds = (int)Math.Log2(size);
        var order = SeedOrder(size);
        var fixtures = new List<FixtureNode>();
        bool thirdPlace = includeThirdPlace && size >= 4;

        for (var r = 1; r <= rounds; r++)
        {
            var matches = size >> r;
            for (var s = 1; s <= matches; s++)
            {
                var isFinal = r == rounds;
                var isSemi = r == rounds - 1;
                fixtures.Add(new FixtureNode
                {
                    Round = roundOffset + r,
                    Slot = s,
                    NextRound = isFinal ? null : roundOffset + r + 1,
                    NextSlot = isFinal ? null : (s + 1) / 2,
                    NextIsHome = s % 2 == 1,
                    LoserNextRound = thirdPlace && isSemi ? roundOffset + rounds : null,
                    LoserNextSlot = thirdPlace && isSemi ? 2 : null,
                    LoserNextIsHome = s % 2 == 1,
                });
            }
        }

        if (thirdPlace)
            fixtures.Add(new FixtureNode { Round = roundOffset + rounds, Slot = 2, IsThirdPlace = true });

        // Round 1 population. Seeds beyond n are byes.
        for (var s = 1; s <= size / 2; s++)
        {
            var f = fixtures.First(x => x.Round == roundOffset + 1 && x.Slot == s && !x.IsThirdPlace);
            var a = order[(s - 1) * 2];
            var b = order[(s - 1) * 2 + 1];
            f.HomeTeamId = a <= n ? seededTeams[a - 1] : null;
            f.AwayTeamId = b <= n ? seededTeams[b - 1] : null;
        }

        BracketAdvancer.Settle(fixtures);
        return new Bracket(fixtures, size, rounds);
    }

    /// <summary>Standard seeding positions: 1 meets size, 2 meets size-1, etc., with top seeds kept apart.</summary>
    public static int[] SeedOrder(int size)
    {
        var order = new List<int> { 1 };
        while (order.Count < size)
        {
            var total = order.Count * 2 + 1;
            order = order.SelectMany(s => new[] { s, total - s }).ToList();
        }
        return order.ToArray();
    }
}
