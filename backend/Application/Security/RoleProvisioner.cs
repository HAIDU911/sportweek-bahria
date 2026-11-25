using SportsWeek.Domain.Enums;

namespace SportsWeek.Application.Security;

public sealed record RoleAssignment(UserRole Role, string Source);

/// <summary>
/// Server-side role mapping. The browser can never choose a role: Admin/Scorer/Stakeholder come only from
/// server configuration allowlists; every other permitted verified user is a Captain.
/// </summary>
public sealed class RoleProvisioner
{
    private readonly HashSet<string> _admins, _scorers, _stakeholders;

    public RoleProvisioner(IEnumerable<string> admins, IEnumerable<string> scorers, IEnumerable<string> stakeholders)
    {
        _admins = Normalize(admins); _scorers = Normalize(scorers); _stakeholders = Normalize(stakeholders);
    }

    public RoleAssignment Resolve(string email)
    {
        var e = Canonical(email);
        if (_admins.Contains(e)) return new(UserRole.Admin, "allowlist:admin");
        if (_scorers.Contains(e)) return new(UserRole.Scorer, "allowlist:scorer");
        if (_stakeholders.Contains(e)) return new(UserRole.Stakeholder, "allowlist:stakeholder");
        return new(UserRole.Captain, "default:captain");
    }

    /// <summary>Lowercase + trim. For Gmail, dots and +tags are ignored so aliases cannot dodge allowlists or duplicate accounts.</summary>
    public static string Canonical(string email)
    {
        var e = (email ?? string.Empty).Trim().ToLowerInvariant();
        var at = e.LastIndexOf('@');
        if (at <= 0) return e;
        var local = e[..at];
        var domain = e[(at + 1)..];
        if (domain is "gmail.com" or "googlemail.com")
        {
            var plus = local.IndexOf('+');
            if (plus >= 0) local = local[..plus];
            local = local.Replace(".", "", StringComparison.Ordinal);
            domain = "gmail.com";
        }
        return $"{local}@{domain}";
    }

    private static HashSet<string> Normalize(IEnumerable<string> xs) =>
        xs.Where(x => !string.IsNullOrWhiteSpace(x)).Select(Canonical).ToHashSet(StringComparer.Ordinal);
}

/// <summary>Configurable domain policy: gmail.com/googlemail.com initially, a Workspace domain later.</summary>
public sealed class EmailDomainPolicy
{
    private readonly HashSet<string> _domains;
    public EmailDomainPolicy(IEnumerable<string> allowedDomains) =>
        _domains = allowedDomains.Select(d => d.Trim().ToLowerInvariant()).Where(d => d.Length > 0).ToHashSet();

    public bool IsAllowed(string email)
    {
        var at = (email ?? string.Empty).Trim().LastIndexOf('@');
        if (at <= 0) return false;
        return _domains.Contains(email!.Trim()[(at + 1)..].ToLowerInvariant());
    }
}
