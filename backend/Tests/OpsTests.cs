using SportsWeek.Application.Abstractions;
using SportsWeek.Application.Services;
using SportsWeek.Contracts;
using SportsWeek.Domain.Engine;
using SportsWeek.Domain.Enums;
using Xunit;

namespace SportsWeek.Tests;

internal sealed class FakeOpsStore : IOpsStore
{
    public sealed class Row { public FixtureNode Node = null!; public int Version = 1; public string? Venue; public DateTime? Start, End; }
    public Guid TournamentId = Guid.NewGuid();
    public string TStatus = "RegistrationClosed";
    public bool Third;
    public Dictionary<Guid, (Guid Owner, string Name, string Status, int Version, DateTime Created)> Teams = [];
    public Dictionary<Guid, Row> Fixtures = [];
    public List<(Guid User, string Type)> Notes = [];

    public Guid AddTeam(string name, string status = "Approved", Guid? owner = null)
    {
        var id = Guid.NewGuid();
        Teams[id] = (owner ?? Guid.NewGuid(), name, status, 1, DateTime.UtcNow.AddMinutes(Teams.Count));
        return id;
    }

    private static FixtureNode Clone(FixtureNode n) => new()
    {
        Id = n.Id, Round = n.Round, Slot = n.Slot, IsThirdPlace = n.IsThirdPlace, NextRound = n.NextRound, NextSlot = n.NextSlot, NextIsHome = n.NextIsHome,
        LoserNextRound = n.LoserNextRound, LoserNextSlot = n.LoserNextSlot, LoserNextIsHome = n.LoserNextIsHome,
        HomeTeamId = n.HomeTeamId, AwayTeamId = n.AwayTeamId, Status = n.Status, WinnerTeamId = n.WinnerTeamId, HomeScore = n.HomeScore, AwayScore = n.AwayScore
    };

    public Task<TeamOpsState?> GetTeamAsync(Guid id, CancellationToken ct) => Task.FromResult<TeamOpsState?>(
        Teams.TryGetValue(id, out var t) ? new TeamOpsState(id, t.Owner, t.Name, t.Status, t.Version, TournamentId, TStatus) : null);

    public Task<bool> SetTeamStatusAsync(Guid id, string status, string? reason, int expectedVersion, CancellationToken ct)
    {
        var t = Teams[id];
        if (expectedVersion >= 0 && t.Version != expectedVersion) return Task.FromResult(false);
        Teams[id] = t with { Status = status, Version = t.Version + 1 };
        return Task.FromResult(true);
    }

    public Task<TournamentOpsState?> GetTournamentAsync(Guid id, CancellationToken ct) =>
        Task.FromResult<TournamentOpsState?>(id == TournamentId ? new TournamentOpsState(id, "T", TStatus, Third, 60) : null);
    public Task<Guid?> CreateTournamentAsync(CreateTournamentRequest r, CancellationToken ct) => Task.FromResult<Guid?>(Guid.NewGuid());
    public Task SetTournamentStatusAsync(Guid id, string status, CancellationToken ct) { TStatus = status; return Task.CompletedTask; }
    public Task<IReadOnlyList<TeamRef>> ListTeamsAsync(Guid tid, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<TeamRef>>(Teams.Select(k => new TeamRef(k.Key, k.Value.Owner, k.Value.Name, k.Value.Status, k.Value.Created)).ToList());
    public Task<IReadOnlyList<OpsTournamentDto>> ListTournamentsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<OpsTournamentDto>>([]);
    public Task<Guid?> GetFixtureTournamentAsync(Guid fid, CancellationToken ct) => Task.FromResult<Guid?>(Fixtures.ContainsKey(fid) ? TournamentId : null);
    public Task<IReadOnlyList<StoredFixture>> ListFixturesAsync(Guid tid, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<StoredFixture>>(Fixtures.Values.Select(r => new StoredFixture(Clone(r.Node), r.Version, r.Venue, r.Start, r.End)).ToList());
    public Task InsertFixturesAsync(Guid tid, IReadOnlyList<FixtureNode> nodes, CancellationToken ct)
    { foreach (var n in nodes) Fixtures[n.Id] = new Row { Node = Clone(n) }; return Task.CompletedTask; }
    public Task DeleteUnresolvedFixturesAsync(Guid tid, CancellationToken ct)
    { foreach (var k in Fixtures.Where(f => !f.Value.Node.Status.IsResolved()).Select(f => f.Key).ToList()) Fixtures.Remove(k); return Task.CompletedTask; }

    public Task<bool> SaveFixturesAsync(IReadOnlyList<(FixtureNode Node, int ExpectedVersion)> changes, CancellationToken ct)
    {
        if (changes.Any(c => Fixtures[c.Node.Id].Version != c.ExpectedVersion)) return Task.FromResult(false);
        foreach (var (n, _) in changes) { var r = Fixtures[n.Id]; r.Node = Clone(n); r.Version++; }
        return Task.FromResult(true);
    }

    public Task<bool> SaveScheduleAsync(Guid fid, string venue, DateTime s, DateTime e, int v, CancellationToken ct)
    {
        var r = Fixtures[fid]; if (r.Version != v) return Task.FromResult(false);
        r.Venue = venue; r.Start = s; r.End = e; r.Node.Status = FixtureStatus.Scheduled; r.Version++; return Task.FromResult(true);
    }
    public Task SetLiveDetailAsync(Guid fid, string? p, string? c, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<ScheduledSlot>> ListSlotsAsync(DateTime a, DateTime b, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ScheduledSlot>>(Fixtures.Values.Where(r => r.Start is not null)
            .Select(r => new ScheduledSlot(r.Node.Id, r.Node.HomeTeamId, r.Node.AwayTeamId, r.Venue, r.Start!.Value, r.End!.Value)).ToList());
    public Task AddNotificationAsync(Guid u, string type, string t, string? b, string? et, string? ei, CancellationToken ct)
    { Notes.Add((u, type)); return Task.CompletedTask; }
}

public class OpsTests
{
    private static Actor Scorer() => new(Guid.NewGuid(), "s@gmail.com", UserRole.Scorer, true, null, "c");
    private static Actor Other(UserRole r) => new(Guid.NewGuid(), "x@gmail.com", r, true, null, "c");
    private static (TournamentOpsService svc, FakeOpsStore st) Make(int teams = 4)
    {
        var st = new FakeOpsStore();
        for (var i = 1; i <= teams; i++) st.AddTeam($"Team {i}");
        return (new TournamentOpsService(st, new FakeAudit()), st);
    }
    private static async Task<List<OpsFixtureDto>> Fx(TournamentOpsService s, FakeOpsStore st) =>
        (await s.ListFixturesAsync(Scorer(), st.TournamentId, default)).Value!.ToList();

    [Theory]
    [InlineData(UserRole.Admin)] [InlineData(UserRole.Captain)] [InlineData(UserRole.Stakeholder)]
    public async Task Only_scorers_can_run_operations_admin_is_read_only(UserRole role)
    {
        var (svc, st) = Make();
        var a = Other(role);
        Assert.Equal(ResultKind.Forbidden, (await svc.GenerateFixturesAsync(a, st.TournamentId, default)).Kind);
        Assert.Equal(ResultKind.Forbidden, (await svc.ReviewTeamAsync(a, st.Teams.Keys.First(), new("approve", null, 1), default)).Kind);
        Assert.Equal(ResultKind.Forbidden, (await svc.SetStatusAsync(a, st.TournamentId, "Cancelled", default)).Kind);
        Assert.Equal(ResultKind.Forbidden, (await svc.RecordResultAsync(a, Guid.NewGuid(), new("Completed", 1, 0, null, 1), default)).Kind);
    }

    [Fact]
    public async Task Review_approves_rejects_with_reason_and_notifies_the_captain()
    {
        var (svc, st) = Make(0);
        var t = st.AddTeam("Falcons", "Pending"); st.TStatus = "RegistrationOpen";
        Assert.Equal(ResultKind.Invalid, (await svc.ReviewTeamAsync(Scorer(), t, new("reject", "no", 1), default)).Kind);
        Assert.Equal(ResultKind.Invalid, (await svc.ReviewTeamAsync(Scorer(), t, new("maybe", null, 1), default)).Kind);
        Assert.Equal(ResultKind.Conflict, (await svc.ReviewTeamAsync(Scorer(), t, new("approve", null, 99), default)).Kind); // stale version
        var ok = await svc.ReviewTeamAsync(Scorer(), t, new("approve", null, 1), default);
        Assert.Equal("Approved", ok.Value);
        Assert.Contains("team_approved", st.Notes.Select(n => n.Type).ToArray()[0]);
        Assert.Equal("team.not_pending", (await svc.ReviewTeamAsync(Scorer(), t, new("approve", null, 2), default)).Code);
    }

    [Fact]
    public async Task Review_is_locked_once_fixtures_exist()
    {
        var (svc, st) = Make(0);
        var t = st.AddTeam("Late", "Pending"); st.TStatus = "InProgress";
        Assert.Equal("tournament.locked", (await svc.ReviewTeamAsync(Scorer(), t, new("approve", null, 1), default)).Code);
    }

    [Fact]
    public async Task Generation_needs_closed_registration_enough_teams_and_runs_once()
    {
        var (svc, st) = Make(4);
        st.TStatus = "RegistrationOpen";
        Assert.Equal("tournament.not_closed", (await svc.GenerateFixturesAsync(Scorer(), st.TournamentId, default)).Code);
        st.TStatus = "RegistrationClosed";
        var r = await svc.GenerateFixturesAsync(Scorer(), st.TournamentId, default);
        Assert.Equal(3, r.Value);
        Assert.Equal("InProgress", st.TStatus);
        Assert.Equal("tournament.not_closed", (await svc.GenerateFixturesAsync(Scorer(), st.TournamentId, default)).Code);

        var (svc2, st2) = Make(1);
        Assert.Equal("fixtures.too_few", (await svc2.GenerateFixturesAsync(Scorer(), st2.TournamentId, default)).Code);
    }

    [Fact]
    public async Task Pending_teams_are_not_seeded_into_the_bracket()
    {
        var (svc, st) = Make(4);
        st.AddTeam("Pending one", "Pending"); st.AddTeam("Rejected one", "Rejected");
        await svc.GenerateFixturesAsync(Scorer(), st.TournamentId, default);
        var ids = (await Fx(svc, st)).SelectMany(f => new[] { f.HomeTeam, f.AwayTeam }).Where(n => n is not null).ToList();
        Assert.True(ids.All(n => n!.StartsWith("Team ")));
    }

    [Fact]
    public async Task A_whole_tournament_can_be_played_to_completion_through_the_service()
    {
        foreach (var n in new[] { 2, 3, 5, 8, 11 })
        {
            var (svc, st) = Make(n); st.Third = n >= 4;
            await svc.GenerateFixturesAsync(Scorer(), st.TournamentId, default);
            var guard = 0;
            while (st.TStatus != "Completed")
            {
                Assert.True(++guard < 200);
                var ready = (await Fx(svc, st)).First(f => f.Status == "Ready");
                var r = await svc.RecordResultAsync(Scorer(), ready.Id, new("Completed", 2, 1, null, ready.Version), default);
                Assert.True(r.IsOk, r.Message);
            }
            var done = await Fx(svc, st);
            var maxRound = done.Max(x => x.Round);
            var final = done.Single(f => f.Round == maxRound && !f.IsThirdPlace);
            Assert.NotNull(final.WinnerTeamId);
        }
    }

    [Fact]
    public async Task Stale_version_is_rejected_and_nothing_is_saved()
    {
        var (svc, st) = Make(4);
        await svc.GenerateFixturesAsync(Scorer(), st.TournamentId, default);
        var f = (await Fx(svc, st)).First(x => x.Status == "Ready");
        Assert.True((await svc.RecordResultAsync(Scorer(), f.Id, new("Completed", 1, 0, null, f.Version), default)).IsOk);
        var again = await svc.RecordResultAsync(Scorer(), f.Id, new("Completed", 0, 5, null, f.Version), default);
        Assert.Equal(ResultKind.Conflict, again.Kind);
        Assert.Equal("Completed", (await Fx(svc, st)).First(x => x.Id == f.Id).Status);
    }

    [Fact]
    public async Task Invalid_results_are_rejected_ties_and_unready_fixtures()
    {
        var (svc, st) = Make(4);
        await svc.GenerateFixturesAsync(Scorer(), st.TournamentId, default);
        var all = await Fx(svc, st);
        var ready = all.First(x => x.Status == "Ready"); var pending = all.First(x => x.Status == "Pending");
        Assert.Equal("score.tie", (await svc.RecordResultAsync(Scorer(), ready.Id, new("Completed", 1, 1, null, ready.Version), default)).Code);
        Assert.Equal("score.missing_participants", (await svc.RecordResultAsync(Scorer(), pending.Id, new("Completed", 1, 0, null, pending.Version), default)).Code);
        Assert.Equal(ResultKind.Invalid, (await svc.RecordResultAsync(Scorer(), ready.Id, new("Banana", 1, 0, null, ready.Version), default)).Kind);
    }

    [Fact]
    public async Task Scheduling_detects_venue_and_team_clashes_but_allows_clean_slots()
    {
        var (svc, st) = Make(4);
        await svc.GenerateFixturesAsync(Scorer(), st.TournamentId, default);
        var ready = (await Fx(svc, st)).Where(x => x.Status == "Ready").ToList();
        var t0 = new DateTime(2026, 12, 1, 9, 0, 0, DateTimeKind.Utc);
        Assert.True((await svc.ScheduleAsync(Scorer(), ready[0].Id, new("Court 1", t0, t0.AddHours(1), ready[0].Version), default)).IsOk);
        var clash = await svc.ScheduleAsync(Scorer(), ready[1].Id, new("court 1", t0.AddMinutes(30), t0.AddMinutes(90), ready[1].Version), default);
        Assert.Equal("schedule.clash", clash.Code);
        Assert.True((await svc.ScheduleAsync(Scorer(), ready[1].Id, new("Court 2", t0.AddMinutes(30), t0.AddMinutes(90), ready[1].Version), default)).IsOk);
        Assert.Equal(ResultKind.Invalid, (await svc.ScheduleAsync(Scorer(), ready[0].Id, new("X", t0, t0.AddHours(-1), 3), default)).Kind);
    }

    [Fact]
    public async Task Withdrawal_gives_the_opponent_a_walkover_and_advances_them()
    {
        var (svc, st) = Make(4);
        await svc.GenerateFixturesAsync(Scorer(), st.TournamentId, default);
        var f = (await Fx(svc, st)).First(x => x.Status == "Ready");
        var res = await svc.WithdrawAsync(Scorer(), f.HomeTeamId!.Value, "injury", default);
        Assert.Equal(1, res.Value!.WalkoversApplied);
        var after = await Fx(svc, st);
        Assert.Equal("Walkover", after.First(x => x.Id == f.Id).Status);
        Assert.Equal(f.AwayTeamId, after.First(x => x.Id == f.Id).WinnerTeamId);
        Assert.Equal("Withdrawn", st.Teams[f.HomeTeamId!.Value].Status);
        Assert.Contains(after, x => x.Round == 2 && (x.HomeTeamId == f.AwayTeamId || x.AwayTeamId == f.AwayTeamId));
    }

    [Fact]
    public async Task Captains_can_withdraw_only_their_own_team()
    {
        var (svc, st) = Make(0);
        var owner = Guid.NewGuid(); var team = st.AddTeam("Mine", "Pending", owner);
        var stranger = new Actor(Guid.NewGuid(), "x@gmail.com", UserRole.Captain, true, null, "c");
        Assert.Equal(ResultKind.Forbidden, (await svc.WithdrawAsync(stranger, team, null, default)).Kind);
        var me = new Actor(owner, "me@gmail.com", UserRole.Captain, true, null, "c");
        Assert.True((await svc.WithdrawAsync(me, team, "changed plans", default)).IsOk);
        Assert.Equal(ResultKind.Conflict, (await svc.WithdrawAsync(me, team, null, default)).Kind);
    }

    [Fact]
    public async Task Regeneration_excludes_withdrawn_and_eliminated_and_keeps_history()
    {
        var (svc, st) = Make(8);
        await svc.GenerateFixturesAsync(Scorer(), st.TournamentId, default);
        var r1 = (await Fx(svc, st)).Where(x => x.Round == 1).ToList();
        foreach (var f in r1.Take(2)) await svc.RecordResultAsync(Scorer(), f.Id, new("Completed", 3, 0, null, f.Version), default);
        var withdrawn = r1[3].HomeTeamId!.Value;
        await svc.WithdrawAsync(Scorer(), withdrawn, null, default);

        var prev = await svc.PreviewRegenerationAsync(Scorer(), st.TournamentId, default);
        Assert.True(prev.IsOk, prev.Message);
        Assert.True(prev.Value!.CanConfirm);
        Assert.Contains(prev.Value.Excluded, e => e.Contains("Withdrawn"));
        Assert.Contains(prev.Value.Excluded, e => e.Contains("Eliminated"));
        var historyBefore = (await Fx(svc, st)).Count(x => x.Status is "Completed" or "Walkover");
        Assert.True((await svc.ConfirmRegenerationAsync(Scorer(), st.TournamentId, default)).IsOk);
        Assert.True((await Fx(svc, st)).Count(x => x.Status is "Completed" or "Walkover") >= historyBefore); // history untouched
    }

    [Fact]
    public async Task Tournament_status_transitions_are_enforced()
    {
        var (svc, st) = Make(4); st.TStatus = "Draft";
        Assert.Equal("tournament.transition", (await svc.SetStatusAsync(Scorer(), st.TournamentId, "InProgress", default)).Code);
        Assert.True((await svc.SetStatusAsync(Scorer(), st.TournamentId, "RegistrationOpen", default)).IsOk);
        Assert.True((await svc.SetStatusAsync(Scorer(), st.TournamentId, "RegistrationClosed", default)).IsOk);
        await svc.GenerateFixturesAsync(Scorer(), st.TournamentId, default);
        Assert.Equal("tournament.transition", (await svc.SetStatusAsync(Scorer(), st.TournamentId, "RegistrationOpen", default)).Code);
    }
}
