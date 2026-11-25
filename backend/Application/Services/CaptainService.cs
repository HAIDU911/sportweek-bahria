using System.Text.RegularExpressions;
using SportsWeek.Application.Abstractions;
using SportsWeek.Contracts;
using SportsWeek.Domain.Enums;

namespace SportsWeek.Application.Services;

/// <summary>Captain use cases: register a team (idempotent, validated) and list owned teams (ownership-scoped).</summary>
public sealed partial class CaptainService
{
    private readonly ICaptainStore _store;
    private readonly IAuditWriter _audit;

    public CaptainService(ICaptainStore store, IAuditWriter audit) { _store = store; _audit = audit; }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9\\-]{4,19}$")]
    private static partial Regex EnrollmentRegex();

    [GeneratedRegex("^[A-Za-z0-9\\-_]{8,64}$")]
    private static partial Regex IdempotencyRegex();

    public async Task<ServiceResult<Guid>> RegisterTeamAsync(
        Actor actor, Guid tournamentId, RegisterTeamRequest req, string? idempotencyKey, CancellationToken ct)
    {
        if (actor.Role != UserRole.Captain) return ServiceResult<Guid>.Forbidden("Only captains can register teams.");
        if (!actor.EmailVerified) return ServiceResult<Guid>.Forbidden("A verified Google account is required to register a team.");

        var rules = await _store.GetTournamentRulesAsync(tournamentId, ct);
        if (rules is null) return ServiceResult<Guid>.NotFound("Tournament not found.");

        var errors = Validate(req, idempotencyKey, rules);
        if (errors.Count > 0) return ServiceResult<Guid>.Invalid(errors.ToDictionary(k => k.Key, v => v.Value.ToArray()));

        if (!rules.RegistrationOpen || rules.Status != nameof(TournamentStatus.RegistrationOpen))
            return ServiceResult<Guid>.Rule("registration.closed", "Registration is not open for this tournament.");
        if (rules.TeamLimit is { } limit && rules.ActiveTeamCount >= limit)
            return ServiceResult<Guid>.Conflict("registration.full", "This tournament has reached its team limit.");

        var players = req.Players.Select(p => new PlayerInput(p.FullName.Trim(), p.EnrollmentNo.Trim().ToUpperInvariant(), p.Department.Trim())).ToList();
        var result = await _store.CreateTeamAsync(
            new CreateTeamCommand(actor.UserId, tournamentId, req.TeamName.Trim(), req.Department.Trim(), players, idempotencyKey!), ct);

        switch (result.Outcome)
        {
            case CreateTeamOutcome.Created:
                await _audit.WriteAsync(new AuditEntry(actor.UserId, "captain.team.registered", "team", result.TeamId.ToString(),
                    "success", actor.CorrelationId, actor.IpAddress), ct);
                return ServiceResult<Guid>.Ok(result.TeamId!.Value);
            case CreateTeamOutcome.IdempotentReplay:
                return ServiceResult<Guid>.Ok(result.TeamId!.Value); // same key → same team, no second insert
            case CreateTeamOutcome.DuplicateTeamName:
                return ServiceResult<Guid>.Conflict("team.duplicate_name", "A team with this name is already registered in this tournament.");
            case CreateTeamOutcome.DuplicatePlayers:
                return ServiceResult<Guid>.Conflict("team.duplicate_players",
                    $"{result.ConflictingEnrollments?.Count ?? 0} player(s) are already registered in this tournament.");
            case CreateTeamOutcome.LimitReached:
                return ServiceResult<Guid>.Conflict("registration.full", "This tournament has reached its team limit.");
            default:
                throw new InvalidOperationException("Unhandled outcome.");
        }
    }

    public async Task<ServiceResult<IReadOnlyList<CaptainTeamDto>>> ListOwnTeamsAsync(Actor actor, CancellationToken ct)
    {
        if (actor.Role != UserRole.Captain) return ServiceResult<IReadOnlyList<CaptainTeamDto>>.Forbidden();
        // Scope is the caller's own id taken from the validated identity, never from a request parameter (IDOR-safe).
        return ServiceResult<IReadOnlyList<CaptainTeamDto>>.Ok(await _store.ListOwnedTeamsAsync(actor.UserId, ct));
    }

    public static Dictionary<string, List<string>> Validate(RegisterTeamRequest req, string? idempotencyKey, TournamentRules rules)
    {
        var e = new Dictionary<string, List<string>>();
        void Add(string k, string m) { if (!e.TryGetValue(k, out var l)) e[k] = l = []; l.Add(m); }

        if (idempotencyKey is null || !IdempotencyRegex().IsMatch(idempotencyKey))
            Add("Idempotency-Key", "A client-generated Idempotency-Key header (8–64 chars) is required.");
        if (string.IsNullOrWhiteSpace(req.TeamName) || req.TeamName.Trim().Length is < 3 or > 60)
            Add("teamName", "Team name must be 3–60 characters.");
        if (string.IsNullOrWhiteSpace(req.Department) || req.Department.Trim().Length is < 2 or > 60)
            Add("department", "Department must be 2–60 characters.");

        var players = req.Players ?? [];
        if (players.Count < rules.MinPlayers || players.Count > rules.MaxPlayers)
            Add("players", $"This sport requires {rules.MinPlayers}–{rules.MaxPlayers} players.");

        var seen = new HashSet<string>();
        for (var i = 0; i < players.Count; i++)
        {
            var p = players[i];
            if (string.IsNullOrWhiteSpace(p.FullName) || p.FullName.Trim().Length is < 2 or > 80)
                Add($"players[{i}].fullName", "Name must be 2–80 characters.");
            if (string.IsNullOrWhiteSpace(p.EnrollmentNo) || !EnrollmentRegex().IsMatch(p.EnrollmentNo.Trim()))
                Add($"players[{i}].enrollmentNo", "Enrollment number must be 5–20 letters, digits or dashes.");
            else if (!seen.Add(p.EnrollmentNo.Trim().ToUpperInvariant()))
                Add($"players[{i}].enrollmentNo", "This player appears more than once in the roster.");
            if (string.IsNullOrWhiteSpace(p.Department) || p.Department.Trim().Length is < 2 or > 60)
                Add($"players[{i}].department", "Department must be 2–60 characters.");
        }
        return e;
    }
}
