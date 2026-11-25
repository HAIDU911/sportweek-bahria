using SportsWeek.Domain.Enums;

namespace SportsWeek.Domain.Engine;

public sealed record MatchResult(MatchOutcome Outcome, Guid? WinnerId, Guid? LoserId, int? HomeScore, int? AwayScore);

public sealed record ScoreValidation(bool IsValid, string? ErrorCode, string? Error, MatchResult? Result)
{
    public static ScoreValidation Fail(string code, string msg) => new(false, code, msg, null);
    public static ScoreValidation Ok(MatchResult r) => new(true, null, null, r);
}

/// <summary>Validates raw scoring input and derives the winner. Winner is never trusted from the client for scored matches.</summary>
public static class ScoreValidator
{
    public static ScoreValidation Validate(
        FixtureNode fixture, MatchOutcome outcome, int? homeScore, int? awayScore, Guid? walkoverWinnerId = null)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        if (fixture.HomeTeamId is null || fixture.AwayTeamId is null)
            return ScoreValidation.Fail("score.missing_participants", "Both participants must be known before a result can be recorded.");
        if (fixture.Status.IsResolved())
            return ScoreValidation.Fail("score.already_resolved", "This fixture is already resolved. Use the authorised correction workflow.");

        var home = fixture.HomeTeamId.Value;
        var away = fixture.AwayTeamId.Value;

        switch (outcome)
        {
            case MatchOutcome.Completed:
                if (homeScore is null || awayScore is null)
                    return ScoreValidation.Fail("score.required", "Both scores are required.");
                if (homeScore < 0 || awayScore < 0)
                    return ScoreValidation.Fail("score.negative", "Scores cannot be negative.");
                if (homeScore == awayScore)
                    return ScoreValidation.Fail("score.tie", "A knockout match cannot end level. Record the deciding score.");
                var homeWon = homeScore > awayScore;
                return ScoreValidation.Ok(new MatchResult(outcome, homeWon ? home : away, homeWon ? away : home, homeScore, awayScore));

            case MatchOutcome.Walkover:
                if (walkoverWinnerId is null || (walkoverWinnerId != home && walkoverWinnerId != away))
                    return ScoreValidation.Fail("score.walkover_winner", "A walkover winner must be one of the two participants.");
                return ScoreValidation.Ok(new MatchResult(outcome, walkoverWinnerId, walkoverWinnerId == home ? away : home, null, null));

            case MatchOutcome.DoubleForfeit:
            case MatchOutcome.Abandoned:
                return ScoreValidation.Ok(new MatchResult(outcome, null, null, null, null));

            default:
                return ScoreValidation.Fail("score.outcome", "Unknown outcome.");
        }
    }
}
