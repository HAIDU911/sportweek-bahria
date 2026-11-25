using SportsWeek.Domain.Common;
using SportsWeek.Domain.Engine;
using SportsWeek.Domain.Enums;
using Xunit;

namespace SportsWeek.Tests;

public class BracketTests
{
    internal static List<Guid> Teams(int n) => Enumerable.Range(0, n).Select(_ => Guid.NewGuid()).ToList();

    [Theory]
    [InlineData(4, new[] { 1, 4, 2, 3 })]
    [InlineData(8, new[] { 1, 8, 4, 5, 2, 7, 3, 6 })]
    public void Seed_order_is_standard(int size, int[] expected) =>
        Assert.Equal(expected, BracketGenerator.SeedOrder(size));

    [Theory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(8)]
    [InlineData(16)] [InlineData(32)] [InlineData(33)] [InlineData(64)]
    public void Bracket_shape_is_correct_for_key_sizes(int n)
    {
        var b = BracketGenerator.Generate(Teams(n));
        var size = 1; while (size < n) size <<= 1;
        Assert.Equal(size, b.Size);
        Assert.Equal(size - 1, b.Fixtures.Count);
        Assert.Equal(size - n, b.Fixtures.Count(f => f.Status == FixtureStatus.Bye));
        Assert.Single(b.Fixtures.Where(f => f.NextRound is null));
    }

    [Fact]
    public void Every_team_appears_exactly_once_in_round_one_for_all_sizes()
    {
        for (var n = 2; n <= 64; n++)
        {
            var teams = Teams(n);
            var b = BracketGenerator.Generate(teams);
            var r1 = b.Fixtures.Where(f => f.Round == 1).SelectMany(f => new[] { f.HomeTeamId, f.AwayTeamId }).Where(x => x is not null).Select(x => x!.Value).ToList();
            Assert.Equal(n, r1.Count);
            Assert.Equal(n, r1.Distinct().Count());
        }
    }

    [Fact]
    public void Top_seed_gets_bye_when_odd()
    {
        var teams = Teams(3);
        var b = BracketGenerator.Generate(teams);
        var bye = b.Fixtures.Single(f => f.Status == FixtureStatus.Bye);
        Assert.Equal(teams[0], bye.WinnerTeamId);
        var final = b.Fixtures.Single(f => f.Round == 2);
        Assert.Equal(teams[0], final.HomeTeamId);
        Assert.Equal(FixtureStatus.Pending, final.Status);
    }

    [Fact]
    public void Rejects_out_of_range_and_duplicates()
    {
        Assert.Throws<DomainException>(() => BracketGenerator.Generate(Teams(1)));
        Assert.Throws<DomainException>(() => BracketGenerator.Generate(Teams(65)));
        var t = Teams(4); t[3] = t[0];
        Assert.Throws<DomainException>(() => BracketGenerator.Generate(t));
    }

    [Fact]
    public void Third_place_match_is_fed_by_semi_final_losers()
    {
        var teams = Teams(4);
        var b = BracketGenerator.Generate(teams, includeThirdPlace: true);
        Assert.Single(b.Fixtures.Where(f => f.IsThirdPlace));
        var semis = b.Fixtures.Where(f => f.Round == 1).ToList();
        foreach (var s in semis)
        {
            var v = ScoreValidator.Validate(s, MatchOutcome.Completed, 2, 1);
            BracketAdvancer.ApplyResult(b.Fixtures, s, v.Result!);
        }
        var third = b.Fixtures.Single(f => f.IsThirdPlace);
        Assert.Equal(2, third.ParticipantCount);
        Assert.Equal(FixtureStatus.Ready, third.Status);
    }

    [Fact]
    public void Generation_is_deterministic()
    {
        var teams = Teams(13);
        var a = BracketGenerator.Generate(teams);
        var b = BracketGenerator.Generate(teams);
        Assert.Equal(a.Fixtures.Select(f => (f.Round, f.Slot, f.HomeTeamId, f.AwayTeamId)).ToList(),
                     b.Fixtures.Select(f => (f.Round, f.Slot, f.HomeTeamId, f.AwayTeamId)).ToList());
    }

    [Fact]
    public void Generating_64_teams_is_fast()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 20; i++) BracketGenerator.Generate(Teams(64), true);
        Assert.True(sw.Elapsed.TotalSeconds < 1, $"took {sw.Elapsed.TotalMilliseconds}ms for 20 generations");
    }
}
