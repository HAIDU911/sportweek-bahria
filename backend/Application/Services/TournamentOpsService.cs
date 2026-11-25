using System.Text.Json;
using SportsWeek.Application.Abstractions;
using SportsWeek.Contracts;
using SportsWeek.Domain.Common;
using SportsWeek.Domain.Engine;
using SportsWeek.Domain.Enums;

namespace SportsWeek.Application.Services;

/// <summary>
/// Tournament operations: team review, fixture generation, scheduling, live scoring, withdrawals, regeneration.
/// Restricted to the Scorer role. The read-only Admin role is deliberately NOT allowed to mutate tournament data.
/// Every mutation: role → state → input → optimistic version → persist → notify → audit.
/// </summary>
public sealed class TournamentOpsService
{
    public static readonly TimeSpan MinRecovery = TimeSpan.FromMinutes(30);

    private readonly IOpsStore _store;
    private readonly IAuditWriter _audit;
    private readonly IAdminReadStore? _reads;

    public TournamentOpsService(IOpsStore store, IAuditWriter audit, IAdminReadStore? reads = null) { _store = store; _audit = audit; _reads = reads; }

    private static bool IsScorer(Actor a) => a.Role == UserRole.Scorer;
    private static ServiceResult<T> No<T>() => ServiceResult<T>.Forbidden("Scorer access required.");

    private Task Audit(Actor a, string action, string type, object id, object? meta = null) =>
        _audit.WriteAsync(new AuditEntry(a.UserId, action, type, id.ToString(), "success", a.CorrelationId, a.IpAddress,
            meta is null ? null : JsonSerializer.Serialize(meta)));

    // ---------------- reads for the operator desk (PII masked) ----------------
    public async Task<ServiceResult<IReadOnlyList<OpsTournamentDto>>> ListTournamentsAsync(Actor actor, CancellationToken ct) =>
        !IsScorer(actor) ? No<IReadOnlyList<OpsTournamentDto>>() : ServiceResult<IReadOnlyList<OpsTournamentDto>>.Ok(await _store.ListTournamentsAsync(ct));

    public async Task<ServiceResult<PagedResult<TeamSummaryDto>>> ListTeamsAsync(Actor actor, TeamQuery q, CancellationToken ct)
    {
        if (!IsScorer(actor) || _reads is null) return No<PagedResult<TeamSummaryDto>>();
        var (page, size) = Paging.Normalize(q.Page, q.PageSize);
        var res = await _reads.ListTeamsAsync(q with { Page = page, PageSize = size }, ct);
        return ServiceResult<PagedResult<TeamSummaryDto>>.Ok(res with { Items = res.Items.Select(t => t with { CaptainEmail = Security.Masking.Email(t.CaptainEmail) }).ToList() });
    }

    public async Task<ServiceResult<TeamDetailDto>> GetTeamAsync(Actor actor, Guid id, CancellationToken ct)
    {
        if (!IsScorer(actor) || _reads is null) return No<TeamDetailDto>();
        var t = await _reads.GetTeamAsync(id, ct);
        if (t is null) return ServiceResult<TeamDetailDto>.NotFound("Team not found.");
        return ServiceResult<TeamDetailDto>.Ok(t with
        {
            Team = t.Team with { CaptainEmail = Security.Masking.Email(t.Team.CaptainEmail) },
            Roster = t.Roster.Select(p => p with { EnrollmentNo = Security.Masking.Enrollment(p.EnrollmentNo) }).ToList()
        });
    }

    // ---------------- tournaments ----------------
    public async Task<ServiceResult<Guid>> CreateTournamentAsync(Actor actor, CreateTournamentRequest r, CancellationToken ct)
    {
        if (!IsScorer(actor)) return No<Guid>();
        var errs = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Trim().Length is < 3 or > 80) errs["name"] = ["Name must be 3–80 characters."];
        if (r.TeamLimit is { } l && (l < 2 || l > 64)) errs["teamLimit"] = ["Team limit must be between 2 and 64."];
        if (r.StartsAtUtc is { } s && r.EndsAtUtc is { } e && e <= s) errs["endsAtUtc"] = ["End must be after start."];
        if (errs.Count > 0) return ServiceResult<Guid>.Invalid(errs);
        var id = await _store.CreateTournamentAsync(r with { Name = r.Name.Trim() }, ct);
        if (id is null) return ServiceResult<Guid>.Conflict("tournament.duplicate", "A tournament with this name already exists for this sport.");
        await Audit(actor, "ops.tournament.created", "tournament", id.Value);
        return ServiceResult<Guid>.Ok(id.Value);
    }

    private static readonly Dictionary<string, string[]> Transitions = new()
    {
        ["Draft"] = ["RegistrationOpen", "Cancelled"],
        ["RegistrationOpen"] = ["RegistrationClosed", "Cancelled"],
        ["RegistrationClosed"] = ["RegistrationOpen", "Cancelled"],   // reopen allowed until fixtures exist
        ["InProgress"] = ["Cancelled"],
        ["Completed"] = [],
        ["Cancelled"] = []
    };

    public async Task<ServiceResult<string>> SetStatusAsync(Actor actor, Guid tournamentId, string status, CancellationToken ct)
    {
        if (!IsScorer(actor)) return No<string>();
        var t = await _store.GetTournamentAsync(tournamentId, ct);
        if (t is null) return ServiceResult<string>.NotFound("Tournament not found.");
        if (!Transitions.TryGetValue(t.Status, out var allowed) || !allowed.Contains(status))
            return ServiceResult<string>.Rule("tournament.transition", $"Cannot move a tournament from {t.Status} to {status}.");
        if (t.Status == "RegistrationClosed" && status == "RegistrationOpen" && (await _store.ListFixturesAsync(tournamentId, ct)).Count > 0)
            return ServiceResult<string>.Rule("tournament.has_fixtures", "Registration cannot reopen after fixtures were generated.");
        await _store.SetTournamentStatusAsync(tournamentId, status, ct);
        await Audit(actor, "ops.tournament.status", "tournament", tournamentId, new { from = t.Status, to = status });
        return ServiceResult<string>.Ok(status);
    }

    // ---------------- team review ----------------
    public async Task<ServiceResult<string>> ReviewTeamAsync(Actor actor, Guid teamId, ReviewTeamRequest r, CancellationToken ct)
    {
        if (!IsScorer(actor)) return No<string>();
        var approve = string.Equals(r.Decision, "approve", StringComparison.OrdinalIgnoreCase);
        var reject = string.Equals(r.Decision, "reject", StringComparison.OrdinalIgnoreCase);
        if (!approve && !reject)
            return ServiceResult<string>.Invalid(new Dictionary<string, string[]> { ["decision"] = ["Decision must be 'approve' or 'reject'."] });
        if (reject && (r.Reason?.Trim().Length ?? 0) is < 5 or > 300)
            return ServiceResult<string>.Invalid(new Dictionary<string, string[]> { ["reason"] = ["A rejection reason of 5–300 characters is required."] });

        var team = await _store.GetTeamAsync(teamId, ct);
        if (team is null) return ServiceResult<string>.NotFound("Team not found.");
        if (team.Status != nameof(TeamStatus.Pending))
            return ServiceResult<string>.Conflict("team.not_pending", $"This team is already {team.Status}.");
        if (team.TournamentStatus is "InProgress" or "Completed" or "Cancelled")
            return ServiceResult<string>.Rule("tournament.locked", "Teams cannot be reviewed once fixtures exist. Use regeneration for late entries.");

        var status = approve ? nameof(TeamStatus.Approved) : nameof(TeamStatus.Rejected);
        if (!await _store.SetTeamStatusAsync(teamId, status, reject ? r.Reason!.Trim() : null, r.Version, ct))
            return ServiceResult<string>.Conflict("version_conflict", "Someone else changed this team. Reload and try again.");

        await _store.AddNotificationAsync(team.OwnerId, approve ? "team_approved" : "team_rejected",
            approve ? $"{team.Name} was approved" : $"{team.Name} was rejected", reject ? r.Reason!.Trim() : "You're in. Fixtures will be published after registration closes.",
            "team", teamId.ToString(), ct);
        await Audit(actor, approve ? "ops.team.approved" : "ops.team.rejected", "team", teamId);
        return ServiceResult<string>.Ok(status);
    }

    // ---------------- fixtures ----------------
    public async Task<ServiceResult<int>> GenerateFixturesAsync(Actor actor, Guid tournamentId, CancellationToken ct)
    {
        if (!IsScorer(actor)) return No<int>();
        var t = await _store.GetTournamentAsync(tournamentId, ct);
        if (t is null) return ServiceResult<int>.NotFound("Tournament not found.");
        if (t.Status != "RegistrationClosed")
            return ServiceResult<int>.Rule("tournament.not_closed", "Close registration before generating fixtures.");
        if ((await _store.ListFixturesAsync(tournamentId, ct)).Count > 0)
            return ServiceResult<int>.Conflict("fixtures.exist", "Fixtures already exist. Use regeneration to change the bracket.");

        var seeded = (await _store.ListTeamsAsync(tournamentId, ct)).Where(x => x.Status == nameof(TeamStatus.Approved))
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).Select(x => x.Id).ToList();
        if (seeded.Count < BracketGenerator.MinTeams)
            return ServiceResult<int>.Rule("fixtures.too_few", $"At least {BracketGenerator.MinTeams} approved teams are required (have {seeded.Count}).");

        Bracket bracket;
        try { bracket = BracketGenerator.Generate(seeded, t.ThirdPlace); }
        catch (DomainException ex) { return ServiceResult<int>.Rule(ex.Code, ex.Message); }

        await _store.InsertFixturesAsync(tournamentId, bracket.Fixtures.ToList(), ct);
        await _store.SetTournamentStatusAsync(tournamentId, "InProgress", ct);
        await Audit(actor, "ops.fixtures.generated", "tournament", tournamentId, new { teams = seeded.Count, fixtures = bracket.Fixtures.Count });
        return ServiceResult<int>.Ok(bracket.Fixtures.Count);
    }

    public async Task<ServiceResult<IReadOnlyList<OpsFixtureDto>>> ListFixturesAsync(Actor actor, Guid tournamentId, CancellationToken ct)
    {
        if (!IsScorer(actor)) return No<IReadOnlyList<OpsFixtureDto>>();
        var teams = (await _store.ListTeamsAsync(tournamentId, ct)).ToDictionary(x => x.Id, x => x.Name);
        var fixtures = await _store.ListFixturesAsync(tournamentId, ct);
        string? N(Guid? id) => id is { } g && teams.TryGetValue(g, out var n) ? n : null;
        return ServiceResult<IReadOnlyList<OpsFixtureDto>>.Ok(fixtures.OrderBy(f => f.Node.Round).ThenBy(f => f.Node.IsThirdPlace).ThenBy(f => f.Node.Slot)
            .Select(f => new OpsFixtureDto(f.Node.Id, f.Node.Round, f.Node.Slot, f.Node.IsThirdPlace, f.Node.Status.ToString(),
                f.Node.HomeTeamId, N(f.Node.HomeTeamId), f.Node.AwayTeamId, N(f.Node.AwayTeamId), f.Node.HomeScore, f.Node.AwayScore,
                f.Node.WinnerTeamId, f.Venue, f.StartsAtUtc, f.EndsAtUtc, f.Version)).ToList());
    }

    private async Task<(IReadOnlyList<StoredFixture>? all, StoredFixture? fx, Guid tid)> LoadAsync(Guid fixtureId, CancellationToken ct)
    {
        var tid = await _store.GetFixtureTournamentAsync(fixtureId, ct);
        if (tid is null) return (null, null, Guid.Empty);
        var all = await _store.ListFixturesAsync(tid.Value, ct);
        return (all, all.FirstOrDefault(x => x.Node.Id == fixtureId), tid.Value);
    }

    public async Task<ServiceResult<bool>> ScheduleAsync(Actor actor, Guid fixtureId, ScheduleRequest r, CancellationToken ct)
    {
        if (!IsScorer(actor)) return No<bool>();
        var errs = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(r.Venue) || r.Venue.Trim().Length is < 2 or > 60) errs["venue"] = ["Venue must be 2–60 characters."];
        if (r.EndsAtUtc <= r.StartsAtUtc) errs["endsAtUtc"] = ["End must be after start."];
        if (errs.Count > 0) return ServiceResult<bool>.Invalid(errs);

        var (_, fx, _) = await LoadAsync(fixtureId, ct);
        if (fx is null) return ServiceResult<bool>.NotFound("Fixture not found.");
        if (fx.Node.Status is not (FixtureStatus.Ready or FixtureStatus.Scheduled))
            return ServiceResult<bool>.Rule("fixture.not_schedulable", "Only fixtures with both teams known and not yet played can be scheduled.");

        var venue = r.Venue.Trim();
        var cand = new ScheduledSlot(fixtureId, fx.Node.HomeTeamId, fx.Node.AwayTeamId, venue, r.StartsAtUtc, r.EndsAtUtc);
        var existing = await _store.ListSlotsAsync(r.StartsAtUtc.AddDays(-1), r.EndsAtUtc.AddDays(1), ct);
        var clashes = ScheduleClashDetector.Detect(cand, existing, MinRecovery);
        if (clashes.Count > 0)
            return ServiceResult<bool>.Conflict("schedule.clash", string.Join(" ", clashes.Select(c => c.Message).Distinct()));

        if (!await _store.SaveScheduleAsync(fixtureId, venue, r.StartsAtUtc, r.EndsAtUtc, r.Version, ct))
            return ServiceResult<bool>.Conflict("version_conflict", "This fixture changed. Reload and try again.");

        await NotifyCaptains(fx, "fixture_scheduled", "Match scheduled", $"{venue} · {r.StartsAtUtc:u}", fixtureId, ct);
        await Audit(actor, "ops.fixture.scheduled", "fixture", fixtureId, new { venue });
        return ServiceResult<bool>.Ok(true);
    }

    public async Task<ServiceResult<bool>> StartAsync(Actor actor, Guid fixtureId, int version, CancellationToken ct)
    {
        if (!IsScorer(actor)) return No<bool>();
        var (_, fx, _) = await LoadAsync(fixtureId, ct);
        if (fx is null) return ServiceResult<bool>.NotFound("Fixture not found.");
        if (fx.Node.Status is not (FixtureStatus.Ready or FixtureStatus.Scheduled))
            return ServiceResult<bool>.Rule("fixture.not_startable", "Only a ready or scheduled fixture can go live.");
        fx.Node.Status = FixtureStatus.Live;
        if (!await _store.SaveFixturesAsync([(fx.Node, version)], ct))
            return ServiceResult<bool>.Conflict("version_conflict", "This fixture changed. Reload and try again.");
        await Audit(actor, "ops.fixture.started", "fixture", fixtureId);
        return ServiceResult<bool>.Ok(true);
    }

    public async Task<ServiceResult<bool>> SetLiveDetailAsync(Actor actor, Guid fixtureId, LiveDetailRequest r, CancellationToken ct)
    {
        if (!IsScorer(actor)) return No<bool>();
        if ((r.Period?.Length ?? 0) > 30 || (r.Clock?.Length ?? 0) > 12)
            return ServiceResult<bool>.Invalid(new Dictionary<string, string[]> { ["period"] = ["Period ≤ 30 and clock ≤ 12 characters."] });
        var (_, fx, _) = await LoadAsync(fixtureId, ct);
        if (fx is null) return ServiceResult<bool>.NotFound("Fixture not found.");
        if (fx.Node.Status != FixtureStatus.Live) return ServiceResult<bool>.Rule("fixture.not_live", "Live detail can only be set while a match is live.");
        await _store.SetLiveDetailAsync(fixtureId, r.Period?.Trim(), r.Clock?.Trim(), ct);
        return ServiceResult<bool>.Ok(true);
    }

    public async Task<ServiceResult<bool>> RecordResultAsync(Actor actor, Guid fixtureId, ResultRequest r, CancellationToken ct)
    {
        if (!IsScorer(actor)) return No<bool>();
        if (!Enum.TryParse<MatchOutcome>(r.Outcome, true, out var outcome))
            return ServiceResult<bool>.Invalid(new Dictionary<string, string[]> { ["outcome"] = ["Outcome must be Completed, Walkover, DoubleForfeit or Abandoned."] });

        var (all, fx, tid) = await LoadAsync(fixtureId, ct);
        if (all is null || fx is null) return ServiceResult<bool>.NotFound("Fixture not found.");
        if (fx.Version != r.Version) return ServiceResult<bool>.Conflict("version_conflict", "This fixture changed. Reload and try again.");

        var v = ScoreValidator.Validate(fx.Node, outcome, r.HomeScore, r.AwayScore, r.WalkoverWinnerId);
        if (!v.IsValid) return ServiceResult<bool>.Rule(v.ErrorCode!, v.Error!);

        var nodes = all.Select(x => x.Node).ToList();
        var before = Snapshot(all);
        try { BracketAdvancer.ApplyResult(nodes, fx.Node, v.Result!); }
        catch (DomainException ex) { return ServiceResult<bool>.Rule(ex.Code, ex.Message); }

        var changes = ChangedSince(all, before);
        if (!await _store.SaveFixturesAsync(changes, ct))
            return ServiceResult<bool>.Conflict("version_conflict", "The bracket changed while saving. Reload and try again.");

        if (nodes.All(n => n.Status.IsResolved())) await _store.SetTournamentStatusAsync(tid, "Completed", ct);
        await NotifyCaptains(fx, "match_result", "Match result recorded", Describe(fx.Node, v.Result!), fixtureId, ct);
        await Audit(actor, "ops.result.recorded", "fixture", fixtureId, new { outcome = outcome.ToString(), r.HomeScore, r.AwayScore });
        return ServiceResult<bool>.Ok(true);
    }

    // ---------------- withdrawal ----------------
    /// <summary>Captains may withdraw their own team; Scorers may withdraw any. Opponents of unresolved fixtures get walkovers.</summary>
    public async Task<ServiceResult<WithdrawResultDto>> WithdrawAsync(Actor actor, Guid teamId, string? reason, CancellationToken ct)
    {
        var team = await _store.GetTeamAsync(teamId, ct);
        if (team is null) return ServiceResult<WithdrawResultDto>.NotFound("Team not found.");
        var allowed = actor.Role == UserRole.Scorer || (actor.Role == UserRole.Captain && team.OwnerId == actor.UserId);
        if (!allowed) return ServiceResult<WithdrawResultDto>.Forbidden("You cannot withdraw this team.");
        if (team.Status is not (nameof(TeamStatus.Pending) or nameof(TeamStatus.Approved)))
            return ServiceResult<WithdrawResultDto>.Conflict("team.not_withdrawable", $"A {team.Status} team cannot be withdrawn.");
        if (team.TournamentStatus is "Completed" or "Cancelled")
            return ServiceResult<WithdrawResultDto>.Rule("tournament.closed", "This tournament has finished.");

        if (!await _store.SetTeamStatusAsync(teamId, nameof(TeamStatus.Withdrawn), reason?.Trim(), -1, ct))
            return ServiceResult<WithdrawResultDto>.NotFound("Team not found.");

        int walkovers = 0, manual = 0;
        if (team.TournamentStatus == "InProgress")
        {
            var all = await _store.ListFixturesAsync(team.TournamentId, ct);
            var nodes = all.Select(x => x.Node).ToList();
            var before = Snapshot(all);
            foreach (var n in nodes.Where(n => !n.Status.IsResolved() && (n.HomeTeamId == teamId || n.AwayTeamId == teamId)).ToList())
            {
                if (n.Status == FixtureStatus.Live) { manual++; continue; }
                if (n.ParticipantCount == 2)
                {
                    var opp = n.HomeTeamId == teamId ? n.AwayTeamId : n.HomeTeamId;
                    var res = ScoreValidator.Validate(n, MatchOutcome.Walkover, null, null, opp);
                    if (res.IsValid) { BracketAdvancer.ApplyResult(nodes, n, res.Result!); walkovers++; continue; }
                }
                manual++; // opponent not decided yet: operator records the walkover once it is
            }
            var changes = ChangedSince(all, before);
            if (changes.Count > 0 && !await _store.SaveFixturesAsync(changes, ct))
                return ServiceResult<WithdrawResultDto>.Conflict("version_conflict", "The bracket changed while withdrawing. Try again.");
        }

        await _store.AddNotificationAsync(team.OwnerId, "team_withdrawn", $"{team.Name} was withdrawn", reason, "team", teamId.ToString(), ct);
        await Audit(actor, "team.withdrawn", "team", teamId, new { by = actor.Role.ToString(), walkovers, manual });
        return ServiceResult<WithdrawResultDto>.Ok(new WithdrawResultDto(walkovers, manual));
    }

    // ---------------- regeneration ----------------
    public async Task<ServiceResult<RegenerationPreviewDto>> PreviewRegenerationAsync(Actor actor, Guid tournamentId, CancellationToken ct)
    {
        if (!IsScorer(actor)) return No<RegenerationPreviewDto>();
        var built = await BuildPlanAsync(tournamentId, ct);
        return built.err is not null ? ServiceResult<RegenerationPreviewDto>.Rule(built.err.Value.code, built.err.Value.msg)
            : ServiceResult<RegenerationPreviewDto>.Ok(built.preview!);
    }

    public async Task<ServiceResult<int>> ConfirmRegenerationAsync(Actor actor, Guid tournamentId, CancellationToken ct)
    {
        if (!IsScorer(actor)) return No<int>();
        var built = await BuildPlanAsync(tournamentId, ct);
        if (built.err is not null) return ServiceResult<int>.Rule(built.err.Value.code, built.err.Value.msg);
        if (built.plan?.ContinuationBracket is null) return ServiceResult<int>.Rule("regen.too_few", "Fewer than two eligible teams remain.");

        await _store.DeleteUnresolvedFixturesAsync(tournamentId, ct);   // resolved history is protected by DB trigger
        await _store.InsertFixturesAsync(tournamentId, built.plan.ContinuationBracket.Fixtures.ToList(), ct);
        await Audit(actor, "ops.fixtures.regenerated", "tournament", tournamentId, new { included = built.plan.IncludedTeams.Count });
        return ServiceResult<int>.Ok(built.plan.ContinuationBracket.Fixtures.Count);
    }

    private async Task<(RegenerationPlan? plan, RegenerationPreviewDto? preview, (string code, string msg)? err)> BuildPlanAsync(Guid tid, CancellationToken ct)
    {
        var t = await _store.GetTournamentAsync(tid, ct);
        if (t is null) return (null, null, ("not_found", "Tournament not found."));
        if (t.Status != "InProgress") return (null, null, ("regen.not_in_progress", "Regeneration applies only to a tournament in progress."));

        var fixtures = await _store.ListFixturesAsync(tid, ct);
        if (fixtures.Any(f => f.Node.Status == FixtureStatus.Live))
            return (null, null, ("regen.live_match", "Finish or abandon live matches before regenerating."));

        var refs = await _store.ListTeamsAsync(tid, ct);
        var seeded = refs.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).Select((x, i) =>
            new TeamState(x.Id, i + 1, Enum.Parse<TeamStatus>(x.Status))).ToList();
        var resolved = fixtures.Where(f => f.Node.Status.IsResolved()).Select(f => f.Node).ToList();
        var history = resolved.Where(n => n.Status != FixtureStatus.Abandoned).Select(n => n.Status switch
        {
            FixtureStatus.Bye => new PlayedMatch(n.WinnerTeamId!.Value, null, n.WinnerTeamId),
            FixtureStatus.DoubleForfeit => n.ParticipantCount == 2 ? new PlayedMatch(n.HomeTeamId!.Value, n.AwayTeamId, null) : null,
            _ => new PlayedMatch(n.HomeTeamId!.Value, n.AwayTeamId, n.WinnerTeamId)
        }).Where(p => p is not null).Select(p => p!).ToList();
        var last = resolved.Count == 0 ? 0 : resolved.Max(n => n.Round);

        RegenerationPlan plan;
        try { plan = RegenerationPlanner.Plan(seeded, history, last, t.ThirdPlace); }
        catch (DomainException ex) { return (null, null, (ex.Code, ex.Message)); }

        var names = refs.ToDictionary(x => x.Id, x => x.Name);
        var preview = new RegenerationPreviewDto(
            plan.IncludedTeams.Select(i => names[i]).ToList(),
            plan.Excluded.Select(e => $"{names[e.TeamId]} ({e.Reason})").ToList(),
            plan.ContinuationBracket?.Fixtures.Count ?? 0, plan.ContinuationBracket is not null,
            plan.ContinuationBracket is null ? "Fewer than two eligible teams remain." : null);
        return (plan, preview, null);
    }

    // ---------------- helpers ----------------
    private static Dictionary<Guid, (FixtureStatus s, Guid? h, Guid? a, Guid? w, int? hs, int? as_)> Snapshot(IReadOnlyList<StoredFixture> all) =>
        all.ToDictionary(x => x.Node.Id, x => (x.Node.Status, x.Node.HomeTeamId, x.Node.AwayTeamId, x.Node.WinnerTeamId, x.Node.HomeScore, x.Node.AwayScore));

    private static List<(FixtureNode, int)> ChangedSince(IReadOnlyList<StoredFixture> all,
        Dictionary<Guid, (FixtureStatus s, Guid? h, Guid? a, Guid? w, int? hs, int? as_)> before) =>
        all.Where(x => before[x.Node.Id] != (x.Node.Status, x.Node.HomeTeamId, x.Node.AwayTeamId, x.Node.WinnerTeamId, x.Node.HomeScore, x.Node.AwayScore))
           .Select(x => (x.Node, x.Version)).ToList();

    private async Task NotifyCaptains(StoredFixture fx, string type, string title, string body, Guid fixtureId, CancellationToken ct)
    {
        var teams = (await _store.ListTeamsAsync(await _store.GetFixtureTournamentAsync(fixtureId, ct) ?? Guid.Empty, ct)).ToDictionary(x => x.Id);
        foreach (var id in new[] { fx.Node.HomeTeamId, fx.Node.AwayTeamId }.Where(i => i is not null).Select(i => i!.Value).Distinct())
            if (teams.TryGetValue(id, out var t)) await _store.AddNotificationAsync(t.OwnerId, type, title, body, "fixture", fixtureId.ToString(), ct);
    }

    private static string Describe(FixtureNode n, MatchResult r) => r.Outcome switch
    {
        MatchOutcome.Completed => $"Final score {r.HomeScore}–{r.AwayScore}.",
        MatchOutcome.Walkover => "Decided by walkover.",
        MatchOutcome.DoubleForfeit => "Both teams forfeited.",
        _ => "The match was abandoned."
    };
}
