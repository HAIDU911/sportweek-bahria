using SportsWeek.Domain.Engine;
using SportsWeek.Domain.Enums;
using Xunit;

namespace SportsWeek.Tests;

public class RegenerationTests
{
    private static TeamState T(Guid id, int seed, TeamStatus s = TeamStatus.Approved) => new(id, seed, s);

    [Fact]
    public void Excludes_withdrawn_rejected_pending_and_eliminated_but_includes_never_played()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        var d = Guid.NewGuid(); var e = Guid.NewGuid(); var f = Guid.NewGuid(); var g = Guid.NewGuid();
        var teams = new[] { T(a, 1), T(b, 2), T(c, 3), T(d, 4, TeamStatus.Withdrawn), T(e, 5, TeamStatus.Rejected), T(f, 6, TeamStatus.Pending), T(g, 7) };
        var history = new[] { new PlayedMatch(a, b, a) }; // b lost; c, g never played

        var plan = RegenerationPlanner.Plan(teams, history, lastRoundPlayed: 1);

        Assert.Equal(new[] { a, c, g }.OrderBy(x => x), plan.IncludedTeams.OrderBy(x => x));
        Assert.Contains(plan.Excluded, x => x.TeamId == b && x.Reason == ExclusionReason.Eliminated);
        Assert.Contains(plan.Excluded, x => x.TeamId == d && x.Reason == ExclusionReason.Withdrawn);
        Assert.Contains(plan.Excluded, x => x.TeamId == e && x.Reason == ExclusionReason.Rejected);
        Assert.Contains(plan.Excluded, x => x.TeamId == f && x.Reason == ExclusionReason.NotApproved);
        Assert.NotNull(plan.ContinuationBracket);
        Assert.All(plan.ContinuationBracket!.Fixtures, x => Assert.True(x.Round >= 2)); // history rounds untouched
    }

    [Fact]
    public void Walkover_loser_and_double_forfeit_teams_are_out_but_bye_team_continues()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid(); var d = Guid.NewGuid(); var e = Guid.NewGuid();
        var teams = new[] { T(a, 1), T(b, 2), T(c, 3), T(d, 4), T(e, 5) };
        var history = new[] { new PlayedMatch(a, b, b), new PlayedMatch(c, d, null), new PlayedMatch(e, null, e) };
        var plan = RegenerationPlanner.Plan(teams, history, 1);
        Assert.Equal(new[] { b, e }.OrderBy(x => x), plan.IncludedTeams.OrderBy(x => x));
    }

    [Fact]
    public void Fewer_than_two_survivors_returns_no_bracket()
    {
        var a = Guid.NewGuid();
        var plan = RegenerationPlanner.Plan(new[] { T(a, 1) }, Array.Empty<PlayedMatch>(), 1);
        Assert.Null(plan.ContinuationBracket);
    }

    [Fact]
    public void Never_replays_a_completed_matchup()
    {
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToList();
        var played = new HashSet<(Guid, Guid)> { RegenerationPlanner.Key(ids[0], ids[5]), RegenerationPlanner.Key(ids[1], ids[4]) };
        var arranged = RegenerationPlanner.ArrangeAvoidingRematches(ids, played);
        var b = BracketGenerator.Generate(arranged, false, 1);
        foreach (var f in b.Fixtures.Where(x => x.ParticipantCount == 2))
            Assert.False(played.Contains(RegenerationPlanner.Key(f.HomeTeamId!.Value, f.AwayTeamId!.Value)));
        Assert.Equal(6, arranged.Distinct().Count());
    }

    [Fact]
    public void Same_input_gives_same_plan_so_preview_and_confirm_agree()
    {
        var teams = Enumerable.Range(1, 9).Select(i => T(Guid.NewGuid(), i)).ToList();
        var p1 = RegenerationPlanner.Plan(teams, Array.Empty<PlayedMatch>(), 0);
        var p2 = RegenerationPlanner.Plan(teams, Array.Empty<PlayedMatch>(), 0);
        Assert.Equal(p1.IncludedTeams, p2.IncludedTeams);
        Assert.Equal(p1.ContinuationBracket!.Fixtures.Select(f => (f.Round, f.Slot, f.HomeTeamId, f.AwayTeamId)).ToList(),
                     p2.ContinuationBracket!.Fixtures.Select(f => (f.Round, f.Slot, f.HomeTeamId, f.AwayTeamId)).ToList());
    }
}

public class ScheduleClashTests
{
    private static readonly DateTime D = new(2026, 12, 1, 9, 0, 0, DateTimeKind.Utc);
    private static ScheduledSlot S(Guid? h, Guid? a, string? v, int startMin, int len = 60) =>
        new(Guid.NewGuid(), h, a, v, D.AddMinutes(startMin), D.AddMinutes(startMin + len));

    [Fact]
    public void Detects_venue_overlap_but_not_back_to_back()
    {
        var existing = new[] { S(Guid.NewGuid(), Guid.NewGuid(), "Court 1", 0) };
        Assert.Contains(ScheduleClashDetector.Detect(S(Guid.NewGuid(), Guid.NewGuid(), "court 1", 30), existing, TimeSpan.Zero), c => c.Kind == ClashKind.Venue);
        Assert.Empty(ScheduleClashDetector.Detect(S(Guid.NewGuid(), Guid.NewGuid(), "Court 1", 60), existing, TimeSpan.Zero));
    }

    [Fact]
    public void Detects_team_overlap_and_recovery_gap()
    {
        var t = Guid.NewGuid();
        var existing = new[] { S(t, Guid.NewGuid(), "A", 0) };
        Assert.Contains(ScheduleClashDetector.Detect(S(t, Guid.NewGuid(), "B", 10), existing, TimeSpan.FromMinutes(30)), c => c.Kind == ClashKind.Team);
        Assert.Contains(ScheduleClashDetector.Detect(S(t, Guid.NewGuid(), "B", 80), existing, TimeSpan.FromMinutes(30)), c => c.Kind == ClashKind.Recovery);
        Assert.Empty(ScheduleClashDetector.Detect(S(t, Guid.NewGuid(), "B", 120), existing, TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public void Detects_invalid_range_and_window()
    {
        var bad = new ScheduledSlot(Guid.NewGuid(), null, null, null, D, D.AddMinutes(-5));
        Assert.Contains(ScheduleClashDetector.Detect(bad, Array.Empty<ScheduledSlot>(), TimeSpan.Zero), c => c.Kind == ClashKind.InvalidRange);
        var early = S(null, null, null, -120); // 07:00
        var win = new MatchWindow(TimeSpan.FromHours(8), TimeSpan.FromHours(18));
        Assert.Contains(ScheduleClashDetector.Detect(early, Array.Empty<ScheduledSlot>(), TimeSpan.Zero, win), c => c.Kind == ClashKind.OutsideWindow);
    }
}
