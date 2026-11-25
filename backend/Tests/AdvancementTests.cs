using SportsWeek.Domain.Common;
using SportsWeek.Domain.Engine;
using SportsWeek.Domain.Enums;
using Xunit;

namespace SportsWeek.Tests;

public class AdvancementTests
{
    private static void Play(Bracket b, FixtureNode f, int h, int a) =>
        BracketAdvancer.ApplyResult(b.Fixtures, f, ScoreValidator.Validate(f, MatchOutcome.Completed, h, a).Result!);

    [Fact]
    public void Winner_advances_to_the_correct_slot_and_side()
    {
        var t = BracketTests.Teams(4);
        var b = BracketGenerator.Generate(t);
        var m1 = b.Fixtures.Single(f => f.Round == 1 && f.Slot == 1);
        Play(b, m1, 3, 0);
        var final = b.Fixtures.Single(f => f.Round == 2);
        Assert.Equal(m1.HomeTeamId, final.HomeTeamId);
        Assert.Null(final.AwayTeamId);
        Assert.Equal(FixtureStatus.Pending, final.Status);
    }

    [Fact]
    public void Away_winner_advances_and_final_becomes_ready()
    {
        var b = BracketGenerator.Generate(BracketTests.Teams(4));
        Play(b, b.Fixtures.Single(f => f.Round == 1 && f.Slot == 1), 0, 2);
        Play(b, b.Fixtures.Single(f => f.Round == 1 && f.Slot == 2), 5, 4);
        var final = b.Fixtures.Single(f => f.Round == 2);
        Assert.Equal(FixtureStatus.Ready, final.Status);
        Assert.Equal(2, final.ParticipantCount);
    }

    [Fact]
    public void No_phantom_completed_result_when_participant_missing()
    {
        var b = BracketGenerator.Generate(BracketTests.Teams(4));
        var final = b.Fixtures.Single(f => f.Round == 2);
        var fake = new MatchResult(MatchOutcome.Completed, Guid.NewGuid(), null, 1, 0);
        Assert.Throws<DomainException>(() => BracketAdvancer.ApplyResult(b.Fixtures, final, fake));
        Assert.False(ScoreValidator.Validate(final, MatchOutcome.Completed, 1, 0).IsValid);
    }

    [Fact]
    public void Resolved_fixture_cannot_be_overwritten()
    {
        var b = BracketGenerator.Generate(BracketTests.Teams(4));
        var m = b.Fixtures.Single(f => f.Round == 1 && f.Slot == 1);
        Play(b, m, 1, 0);
        Assert.False(ScoreValidator.Validate(m, MatchOutcome.Completed, 0, 9).IsValid);
        var forced = new MatchResult(MatchOutcome.Completed, m.AwayTeamId, m.HomeTeamId, 0, 9);
        Assert.Throws<DomainException>(() => BracketAdvancer.ApplyResult(b.Fixtures, m, forced)); // engine guard, independent of validator
    }

    [Fact]
    public void Winner_must_be_a_participant()
    {
        var b = BracketGenerator.Generate(BracketTests.Teams(4));
        var m = b.Fixtures.Single(f => f.Round == 1 && f.Slot == 1);
        var bad = new MatchResult(MatchOutcome.Completed, Guid.NewGuid(), null, 1, 0);
        Assert.Throws<DomainException>(() => BracketAdvancer.ApplyResult(b.Fixtures, m, bad));
    }

    [Fact]
    public void Double_forfeit_gives_opponent_a_bye_chain()
    {
        var t = BracketTests.Teams(4);
        var b = BracketGenerator.Generate(t);
        var m1 = b.Fixtures.Single(f => f.Round == 1 && f.Slot == 1);
        BracketAdvancer.ApplyResult(b.Fixtures, m1, ScoreValidator.Validate(m1, MatchOutcome.DoubleForfeit, null, null).Result!);
        var m2 = b.Fixtures.Single(f => f.Round == 1 && f.Slot == 2);
        Play(b, m2, 2, 1);
        var final = b.Fixtures.Single(f => f.Round == 2);
        Assert.Equal(FixtureStatus.Bye, final.Status);
        Assert.Equal(m2.WinnerTeamId, final.WinnerTeamId);
    }

    [Theory]
    [InlineData(-1, 2)] [InlineData(2, -1)]
    public void Negative_scores_rejected(int h, int a)
    {
        var b = BracketGenerator.Generate(BracketTests.Teams(4));
        var v = ScoreValidator.Validate(b.Fixtures.First(f => f.Round == 1), MatchOutcome.Completed, h, a);
        Assert.False(v.IsValid);
        Assert.Equal("score.negative", v.ErrorCode);
    }

    [Fact]
    public void Ties_and_missing_scores_rejected_walkover_needs_participant_winner()
    {
        var b = BracketGenerator.Generate(BracketTests.Teams(4));
        var m = b.Fixtures.First(f => f.Round == 1);
        Assert.Equal("score.tie", ScoreValidator.Validate(m, MatchOutcome.Completed, 1, 1).ErrorCode);
        Assert.Equal("score.required", ScoreValidator.Validate(m, MatchOutcome.Completed, null, 1).ErrorCode);
        Assert.Equal("score.walkover_winner", ScoreValidator.Validate(m, MatchOutcome.Walkover, null, null, Guid.NewGuid()).ErrorCode);
        var ok = ScoreValidator.Validate(m, MatchOutcome.Walkover, null, null, m.AwayTeamId);
        Assert.True(ok.IsValid);
        Assert.Equal(m.AwayTeamId, ok.Result!.WinnerId);
    }

    /// <summary>Thousands of randomized tournaments: every team count 2–64, mixed outcomes, invariants checked each time.</summary>
    [Fact]
    public void Randomized_simulation_preserves_all_invariants()
    {
        var rng = new Random(20261122);
        var scenarios = 0;
        for (var n = 2; n <= 64; n++)
        for (var rep = 0; rep < 40; rep++)
        {
            scenarios++;
            var teams = BracketTests.Teams(n);
            var b = BracketGenerator.Generate(teams, includeThirdPlace: rep % 2 == 0);
            var outcomes = new List<(FixtureNode f, Guid? winner)>();
            var guard = 0;
            while (b.Fixtures.Any(f => !f.Status.IsResolved()))
            {
                Assert.True(++guard < 1000, "simulation did not terminate");
                var ready = b.Fixtures.Where(f => f.Status is FixtureStatus.Ready).OrderBy(f => f.Round).ThenBy(f => f.Slot).ToList();
                Assert.NotEmpty(ready);
                var f = ready[rng.Next(ready.Count)];
                var roll = rng.Next(100);
                MatchOutcome o = roll < 80 ? MatchOutcome.Completed : roll < 92 ? MatchOutcome.Walkover : roll < 96 ? MatchOutcome.DoubleForfeit : MatchOutcome.Abandoned;
                var v = o switch
                {
                    MatchOutcome.Completed => rng.Next(2) == 0
                        ? ScoreValidator.Validate(f, o, rng.Next(1, 10), 0)
                        : ScoreValidator.Validate(f, o, 0, rng.Next(1, 10)),
                    MatchOutcome.Walkover => ScoreValidator.Validate(f, o, null, null, rng.Next(2) == 0 ? f.HomeTeamId : f.AwayTeamId),
                    _ => ScoreValidator.Validate(f, o, null, null)
                };
                Assert.True(v.IsValid, v.Error);
                BracketAdvancer.ApplyResult(b.Fixtures, f, v.Result!);
                outcomes.Add((f, f.WinnerTeamId));
            }

            foreach (var f in b.Fixtures)
            {
                // winners are always participants
                if (f.WinnerTeamId is { } w) Assert.True(w == f.HomeTeamId || w == f.AwayTeamId);
                // nobody completed with fewer than two participants
                if (f.Status is FixtureStatus.Completed or FixtureStatus.Walkover) Assert.Equal(2, f.ParticipantCount);
            }
            // a team never occupies two main-bracket fixtures in the same round
            foreach (var g in b.Fixtures.Where(f => !f.IsThirdPlace).GroupBy(f => f.Round))
            {
                var ids = g.SelectMany(f => new[] { f.HomeTeamId, f.AwayTeamId }).Where(x => x is not null).ToList();
                Assert.Equal(ids.Count, ids.Distinct().Count());
            }
        }
        Assert.True(scenarios >= 2400);
    }
}
