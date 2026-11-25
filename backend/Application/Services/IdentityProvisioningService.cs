using System.Text.Json;
using SportsWeek.Application.Abstractions;
using SportsWeek.Application.Security;
using SportsWeek.Domain.Enums;

namespace SportsWeek.Application.Services;

/// <summary>Verified identity claims taken from a *validated* Supabase access token.</summary>
public sealed record IdentityClaims(Guid Subject, string? Email, bool EmailVerified, string? DisplayName, string? AvatarUrl, string? GoogleSubject);

public enum ProvisionOutcome { Ok, NoEmail, DomainNotAllowed, AccountConflict, Inactive }

public sealed record ProvisionResult(ProvisionOutcome Outcome, ProfileSnapshot? Profile);

/// <summary>
/// Maps a validated Google/Supabase identity to a server-owned profile + role.
/// Rules: roles come ONLY from server allowlists; an UNVERIFIED email never receives an elevated role;
/// non-elevated users must be on a permitted domain; role changes and first sign-ins are audited.
/// </summary>
public sealed class IdentityProvisioningService
{
    private readonly RoleProvisioner _roles;
    private readonly EmailDomainPolicy _domains;
    private readonly IProfileStore _profiles;
    private readonly IAuditWriter _audit;

    public IdentityProvisioningService(RoleProvisioner roles, EmailDomainPolicy domains, IProfileStore profiles, IAuditWriter audit)
    { _roles = roles; _domains = domains; _profiles = profiles; _audit = audit; }

    public async Task<ProvisionResult> ProvisionAsync(IdentityClaims c, string? ip, string? correlationId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (string.IsNullOrWhiteSpace(c.Email)) return new(ProvisionOutcome.NoEmail, null);

        var assignment = c.EmailVerified ? _roles.Resolve(c.Email) : new RoleAssignment(UserRole.Captain, "unverified:captain");
        if (assignment.Role == UserRole.Captain && !_domains.IsAllowed(c.Email))
            return new(ProvisionOutcome.DomainNotAllowed, null);

        var name = string.IsNullOrWhiteSpace(c.DisplayName) ? c.Email.Split('@')[0] : c.DisplayName.Trim();
        var res = await _profiles.UpsertAsync(new ProfileUpsert(
            c.Subject, c.GoogleSubject, c.Email.Trim(), RoleProvisioner.Canonical(c.Email), name[..Math.Min(name.Length, 120)],
            c.AvatarUrl, c.EmailVerified, assignment.Role, assignment.Source), ct);

        if (res.Outcome == UpsertOutcome.EmailBelongsToAnotherAccount || res.Profile is null)
            return new(ProvisionOutcome.AccountConflict, null);

        var p = res.Profile;
        if (p.IsNew)
            await _audit.WriteAsync(new AuditEntry(p.Id, "auth.profile_created", "profile", p.Id.ToString(), "success", correlationId, ip,
                JsonSerializer.Serialize(new { role = p.Role.ToString(), source = assignment.Source })), ct);
        else if (p.PreviousRole is { } prev && prev != p.Role)
            await _audit.WriteAsync(new AuditEntry(p.Id, "auth.role_changed", "profile", p.Id.ToString(), "success", correlationId, ip,
                JsonSerializer.Serialize(new { from = prev.ToString(), to = p.Role.ToString(), source = assignment.Source })), ct);

        return p.IsActive ? new(ProvisionOutcome.Ok, p) : new(ProvisionOutcome.Inactive, p);
    }
}
