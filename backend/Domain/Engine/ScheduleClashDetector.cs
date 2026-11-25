namespace SportsWeek.Domain.Engine;

public enum ClashKind { Venue, Team, Recovery, OutsideWindow, InvalidRange }

public sealed record ScheduledSlot(Guid FixtureId, Guid? HomeTeamId, Guid? AwayTeamId, string? Venue, DateTime StartUtc, DateTime EndUtc);

public sealed record Clash(ClashKind Kind, Guid? OtherFixtureId, string Message);

public sealed record MatchWindow(TimeSpan DailyStartUtc, TimeSpan DailyEndUtc);

/// <summary>Detects venue, team, recovery-time and window conflicts BEFORE a schedule is saved.</summary>
public static class ScheduleClashDetector
{
    public static IReadOnlyList<Clash> Detect(
        ScheduledSlot candidate,
        IEnumerable<ScheduledSlot> existing,
        TimeSpan minRecovery,
        MatchWindow? window = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(existing);
        var clashes = new List<Clash>();

        if (candidate.EndUtc <= candidate.StartUtc)
        {
            clashes.Add(new Clash(ClashKind.InvalidRange, null, "End time must be after start time."));
            return clashes;
        }

        if (window is not null)
        {
            var s = candidate.StartUtc.TimeOfDay;
            var e = candidate.EndUtc.TimeOfDay;
            var sameDay = candidate.StartUtc.Date == candidate.EndUtc.Date;
            if (!sameDay || s < window.DailyStartUtc || e > window.DailyEndUtc)
                clashes.Add(new Clash(ClashKind.OutsideWindow, null, "Match falls outside the permitted daily window."));
        }

        var candTeams = Teams(candidate);
        foreach (var other in existing.Where(o => o.FixtureId != candidate.FixtureId))
        {
            var overlap = candidate.StartUtc < other.EndUtc && other.StartUtc < candidate.EndUtc;
            var sharedTeams = candTeams.Intersect(Teams(other)).Any();

            if (overlap && !string.IsNullOrWhiteSpace(candidate.Venue)
                && string.Equals(candidate.Venue, other.Venue, StringComparison.OrdinalIgnoreCase))
                clashes.Add(new Clash(ClashKind.Venue, other.FixtureId, $"Venue '{candidate.Venue}' is already booked at that time."));

            if (overlap && sharedTeams)
                clashes.Add(new Clash(ClashKind.Team, other.FixtureId, "A team is already playing at that time."));
            else if (sharedTeams && !overlap)
            {
                var gap = candidate.StartUtc >= other.EndUtc ? candidate.StartUtc - other.EndUtc : other.StartUtc - candidate.EndUtc;
                if (gap < minRecovery)
                    clashes.Add(new Clash(ClashKind.Recovery, other.FixtureId, $"Teams need at least {minRecovery.TotalMinutes:0} minutes of recovery."));
            }
        }
        return clashes;
    }

    private static HashSet<Guid> Teams(ScheduledSlot s)
    {
        var set = new HashSet<Guid>();
        if (s.HomeTeamId is { } h) set.Add(h);
        if (s.AwayTeamId is { } a) set.Add(a);
        return set;
    }
}
