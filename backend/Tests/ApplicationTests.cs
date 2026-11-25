using SportsWeek.Application.Abstractions;
using SportsWeek.Application.Security;
using SportsWeek.Application.Services;
using SportsWeek.Contracts;
using SportsWeek.Domain.Enums;
using Xunit;

namespace SportsWeek.Tests;

public class SecurityTests
{
    private static readonly RoleProvisioner P = new(["Boss@Gmail.com"], ["scorer@gmail.com"], ["stake@gmail.com"]);

    [Fact]
    public void Allowlists_control_elevated_roles_everyone_else_is_captain()
    {
        Assert.Equal(UserRole.Admin, P.Resolve("boss@gmail.com").Role);
        Assert.Equal(UserRole.Scorer, P.Resolve("scorer@gmail.com").Role);
        Assert.Equal(UserRole.Stakeholder, P.Resolve("stake@gmail.com").Role);
        Assert.Equal(UserRole.Captain, P.Resolve("random.student@gmail.com").Role);
    }

    [Fact]
    public void Gmail_dots_plus_tags_and_case_cannot_dodge_or_spoof_the_allowlist()
    {
        Assert.Equal(UserRole.Admin, P.Resolve("b.o.s.s+evil@googlemail.com").Role); // same mailbox as boss@gmail.com
        Assert.Equal("boss@gmail.com", RoleProvisioner.Canonical(" BOSS@GMAIL.COM "));
        Assert.Equal(UserRole.Captain, P.Resolve("boss@notgmail.com").Role);
    }

    [Fact]
    public void Domain_policy_is_configurable()
    {
        var pol = new EmailDomainPolicy(["gmail.com", "googlemail.com"]);
        Assert.True(pol.IsAllowed("a@gmail.com"));
        Assert.True(pol.IsAllowed("a@GoogleMail.com"));
        Assert.False(pol.IsAllowed("a@evil.com"));
        Assert.False(pol.IsAllowed("not-an-email"));
        Assert.True(new EmailDomainPolicy(["bahria.edu.pk"]).IsAllowed("x@bahria.edu.pk"));
    }

    [Fact]
    public void Masking_hides_pii()
    {
        Assert.Equal("ha***@gmail.com", Masking.Email("hamza@gmail.com"));
        Assert.Equal("a***@x.com", Masking.Email("a@x.com"));
        Assert.Equal("****-1234", Masking.Enrollment("01-134212-1234"));
        Assert.Equal("****", Masking.Enrollment("123"));
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://evil\")", "'=HYPERLINK(\"\"http://evil\"\")")]
    [InlineData("+1+1", "'+1+1")] [InlineData("-2", "'-2")] [InlineData("@SUM(A1)", "'@SUM(A1)")]
    public void Csv_blocks_formula_injection(string input, string expectedContains) =>
        Assert.Contains(expectedContains, CsvBuilder.Escape(input));

    [Fact]
    public void Csv_quotes_commas_and_newlines()
    {
        Assert.Equal("\"a,b\"", CsvBuilder.Escape("a,b"));
        Assert.Equal("\"line1\nline2\"", CsvBuilder.Escape("line1\nline2"));
        Assert.Equal("plain", CsvBuilder.Escape("plain"));
    }
}

internal sealed class FakeAudit : IAuditWriter
{
    public List<AuditEntry> Entries { get; } = [];
    public Task WriteAsync(AuditEntry entry, CancellationToken ct = default) { Entries.Add(entry); return Task.CompletedTask; }
}

internal sealed class FakeAdminStore : IAdminReadStore
{
    public UserQuery? LastUserQuery;
    public Guid UserId = Guid.NewGuid();

    public Task<PagedResult<UserSummaryDto>> ListUsersAsync(UserQuery q, CancellationToken ct)
    {
        LastUserQuery = q;
        return Task.FromResult(new PagedResult<UserSummaryDto>(
            [new UserSummaryDto(UserId, "Hamza", "hamza@gmail.com", "Captain", true, true, 1, null, DateTime.UtcNow)], q.Page, q.PageSize, 1));
    }
    public Task<UserDetailDto?> GetUserAsync(Guid id, CancellationToken ct) => Task.FromResult<UserDetailDto?>(id != UserId ? null :
        new UserDetailDto(id, "Hamza", "hamza@gmail.com", "Captain", "default:captain", true, true, "sub-123", null, DateTime.UtcNow, [], []));
    public Task<PagedResult<ParticipantDto>> ListParticipantsAsync(ParticipantQuery q, CancellationToken ct) =>
        Task.FromResult(new PagedResult<ParticipantDto>(
            [new ParticipantDto(Guid.NewGuid(), "Ali", "01-134212-1234", "CS", Guid.NewGuid(), "Falcons", "Approved", "Futsal", "Futsal 2026", Guid.NewGuid(), "Hamza", DateTime.UtcNow)], q.Page, q.PageSize, 1));
    public Task<PagedResult<TeamSummaryDto>> ListTeamsAsync(TeamQuery q, CancellationToken ct) =>
        Task.FromResult(new PagedResult<TeamSummaryDto>([], q.Page, q.PageSize, 0));
    public Task<TeamDetailDto?> GetTeamAsync(Guid id, CancellationToken ct) => Task.FromResult<TeamDetailDto?>(null);
    public Task<DashboardDto> GetDashboardAsync(CancellationToken ct) =>
        Task.FromResult(new DashboardDto(DateTime.UtcNow, 1, 1, 1, 1, 1, 0, 1, 0, 0, 0, 0, [], [], []));
    public Task<IReadOnlyList<DepartmentStatDto>> GetDepartmentStatsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DepartmentStatDto>>([]);
    public Task<PagedResult<AuditEventDto>> ListAuditAsync(AuditQuery q, CancellationToken ct) =>
        Task.FromResult(new PagedResult<AuditEventDto>([], q.Page, q.PageSize, 0));
}

public class AdminDirectoryServiceTests
{
    private static Actor A(UserRole r) => new(Guid.NewGuid(), "x@gmail.com", r, true, "127.0.0.1", "corr-1");

    private static (AdminDirectoryService svc, FakeAdminStore store, FakeAudit audit) Make()
    {
        var store = new FakeAdminStore(); var audit = new FakeAudit();
        return (new AdminDirectoryService(store, audit), store, audit);
    }

    [Theory]
    [InlineData(UserRole.Captain)] [InlineData(UserRole.Scorer)] [InlineData(UserRole.Stakeholder)]
    public async Task Non_admins_are_forbidden_everywhere(UserRole role)
    {
        var (svc, _, _) = Make();
        var a = A(role);
        Assert.Equal(ResultKind.Forbidden, (await svc.ListUsersAsync(a, new UserQuery(null, null, null, null, null), default)).Kind);
        Assert.Equal(ResultKind.Forbidden, (await svc.ListParticipantsAsync(a, new ParticipantQuery(null, null, null, null, null), default)).Kind);
        Assert.Equal(ResultKind.Forbidden, (await svc.GetDashboardAsync(a, default)).Kind);
        Assert.Equal(ResultKind.Forbidden, (await svc.GetUserAsync(a, Guid.NewGuid(), true, default)).Kind);
        Assert.Equal(ResultKind.Forbidden, (await svc.ExportUsersAsync(a, new UserQuery(null, null, null, null, null), false, null, default)).Kind);
    }

    [Fact]
    public async Task Lists_mask_pii_by_default_and_clamp_page_size()
    {
        var (svc, store, _) = Make();
        var users = await svc.ListUsersAsync(A(UserRole.Admin), new UserQuery(null, null, null, null, null, Page: -5, PageSize: 100000), default);
        Assert.Equal("ha***@gmail.com", users.Value!.Items[0].Email);
        Assert.Equal(1, store.LastUserQuery!.Page);
        Assert.Equal(Paging.MaxPageSize, store.LastUserQuery.PageSize);

        var parts = await svc.ListParticipantsAsync(A(UserRole.Admin), new ParticipantQuery(null, null, null, null, null), default);
        Assert.Equal("****-1234", parts.Value!.Items[0].EnrollmentNo);
    }

    [Fact]
    public async Task Viewing_and_revealing_a_user_is_audited()
    {
        var (svc, store, audit) = Make();
        var admin = A(UserRole.Admin);
        var masked = await svc.GetUserAsync(admin, store.UserId, false, default);
        var revealed = await svc.GetUserAsync(admin, store.UserId, true, default);
        Assert.Equal("ha***@gmail.com", masked.Value!.Email);
        Assert.Equal("hamza@gmail.com", revealed.Value!.Email);
        Assert.Equal(["admin.user.viewed", "admin.user.revealed_email"], audit.Entries.Select(e => e.Action).ToArray());
        Assert.All(audit.Entries, e => Assert.Equal(admin.UserId, e.ActorId));
    }

    [Fact]
    public async Task Unknown_user_is_not_found_and_not_audited()
    {
        var (svc, _, audit) = Make();
        var r = await svc.GetUserAsync(A(UserRole.Admin), Guid.NewGuid(), false, default);
        Assert.Equal(ResultKind.NotFound, r.Kind);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task Sensitive_export_needs_a_reason_and_is_audited()
    {
        var (svc, _, audit) = Make();
        var admin = A(UserRole.Admin);
        var q = new ParticipantQuery(null, null, null, null, null);

        var noReason = await svc.ExportParticipantsAsync(admin, q, true, "short", default);
        Assert.Equal(ResultKind.Invalid, noReason.Kind);
        Assert.Empty(audit.Entries);

        var masked = await svc.ExportParticipantsAsync(admin, q, false, null, default);
        Assert.Contains("****-1234", masked.Value!.Csv);
        Assert.DoesNotContain("01-134212-1234", masked.Value.Csv);

        var full = await svc.ExportParticipantsAsync(admin, q, true, "Disciplinary committee request #42", default);
        Assert.Contains("01-134212-1234", full.Value!.Csv);
        Assert.Equal(2, audit.Entries.Count);
        Assert.Contains("Disciplinary committee request #42", audit.Entries[1].MetadataJson);
    }
}

internal sealed class FakeCaptainStore : ICaptainStore
{
    public TournamentRules? Rules = new(Guid.NewGuid(), "RegistrationOpen", true, 8, 3, 5, 2);
    public CreateTeamResult Next = new(CreateTeamOutcome.Created, Guid.NewGuid());
    public int Calls;

    public Task<TournamentRules?> GetTournamentRulesAsync(Guid id, CancellationToken ct) => Task.FromResult(Rules);
    public Task<CreateTeamResult> CreateTeamAsync(CreateTeamCommand cmd, CancellationToken ct) { Calls++; return Task.FromResult(Next); }
    public Task<IReadOnlyList<CaptainTeamDto>> ListOwnedTeamsAsync(Guid ownerId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CaptainTeamDto>>([]);
}

public class CaptainServiceTests
{
    private static Actor Cap(bool verified = true, UserRole role = UserRole.Captain) =>
        new(Guid.NewGuid(), "c@gmail.com", role, verified, null, "corr");

    private static List<PlayerInput> Players(int n) =>
        Enumerable.Range(1, n).Select(i => new PlayerInput($"Player {i}", $"01-0000{i:00}", "CS")).ToList();

    private static RegisterTeamRequest Req(int players = 4) => new("Falcons", "CS", Players(players));

    [Fact]
    public async Task Unverified_or_non_captain_cannot_register()
    {
        var svc = new CaptainService(new FakeCaptainStore(), new FakeAudit());
        Assert.Equal(ResultKind.Forbidden, (await svc.RegisterTeamAsync(Cap(false), Guid.NewGuid(), Req(), "key-12345678", default)).Kind);
        Assert.Equal(ResultKind.Forbidden, (await svc.RegisterTeamAsync(Cap(true, UserRole.Admin), Guid.NewGuid(), Req(), "key-12345678", default)).Kind);
    }

    [Fact]
    public async Task Valid_registration_creates_team_and_audits()
    {
        var store = new FakeCaptainStore(); var audit = new FakeAudit();
        var r = await new CaptainService(store, audit).RegisterTeamAsync(Cap(), Guid.NewGuid(), Req(), "key-12345678", default);
        Assert.True(r.IsOk);
        Assert.Equal(1, store.Calls);
        Assert.Single(audit.Entries);
    }

    [Fact]
    public async Task Validation_failures_return_field_errors_and_never_hit_the_store()
    {
        var store = new FakeCaptainStore();
        var svc = new CaptainService(store, new FakeAudit());
        var bad = new RegisterTeamRequest("x", "", [new PlayerInput("A", "bad id!", "CS"), new PlayerInput("Bee", "01-000002", "CS"), new PlayerInput("Cee", "01-000002", "CS")]);
        var r = await svc.RegisterTeamAsync(Cap(), Guid.NewGuid(), bad, null, default);
        Assert.Equal(ResultKind.Invalid, r.Kind);
        Assert.True(r.FieldErrors!.ContainsKey("teamName"));
        Assert.True(r.FieldErrors.ContainsKey("department"));
        Assert.True(r.FieldErrors.ContainsKey("Idempotency-Key"));
        Assert.True(r.FieldErrors.ContainsKey("players[0].enrollmentNo"));
        Assert.True(r.FieldErrors.ContainsKey("players[2].enrollmentNo")); // duplicate in roster
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task Roster_size_is_enforced_per_tournament_rules()
    {
        var svc = new CaptainService(new FakeCaptainStore(), new FakeAudit());
        Assert.Equal(ResultKind.Invalid, (await svc.RegisterTeamAsync(Cap(), Guid.NewGuid(), Req(2), "key-12345678", default)).Kind);
        Assert.Equal(ResultKind.Invalid, (await svc.RegisterTeamAsync(Cap(), Guid.NewGuid(), Req(6), "key-12345678", default)).Kind);
    }

    [Fact]
    public async Task Closed_full_and_missing_tournaments_are_rejected()
    {
        var id = Guid.NewGuid();
        var closed = new FakeCaptainStore { Rules = new(id, "RegistrationClosed", false, 8, 3, 5, 2) };
        Assert.Equal(ResultKind.BusinessRule, (await new CaptainService(closed, new FakeAudit()).RegisterTeamAsync(Cap(), id, Req(), "key-12345678", default)).Kind);
        var full = new FakeCaptainStore { Rules = new(id, "RegistrationOpen", true, 8, 3, 5, 8) };
        Assert.Equal(ResultKind.Conflict, (await new CaptainService(full, new FakeAudit()).RegisterTeamAsync(Cap(), id, Req(), "key-12345678", default)).Kind);
        var none = new FakeCaptainStore { Rules = null };
        Assert.Equal(ResultKind.NotFound, (await new CaptainService(none, new FakeAudit()).RegisterTeamAsync(Cap(), id, Req(), "key-12345678", default)).Kind);
    }

    [Fact]
    public async Task Duplicates_are_conflicts_not_false_success_and_replay_is_idempotent()
    {
        var store = new FakeCaptainStore { Next = new(CreateTeamOutcome.DuplicatePlayers, null, ["01-000001"]) };
        var svc = new CaptainService(store, new FakeAudit());
        Assert.Equal(ResultKind.Conflict, (await svc.RegisterTeamAsync(Cap(), Guid.NewGuid(), Req(), "key-12345678", default)).Kind);
        store.Next = new(CreateTeamOutcome.DuplicateTeamName, null);
        Assert.Equal("team.duplicate_name", (await svc.RegisterTeamAsync(Cap(), Guid.NewGuid(), Req(), "key-12345678", default)).Code);

        var existing = Guid.NewGuid();
        var audit = new FakeAudit();
        store.Next = new(CreateTeamOutcome.IdempotentReplay, existing);
        var replay = await new CaptainService(store, audit).RegisterTeamAsync(Cap(), Guid.NewGuid(), Req(), "key-12345678", default);
        Assert.Equal(existing, replay.Value);
        Assert.Empty(audit.Entries); // replay is not a second event
    }
}

internal sealed class FakeProfileStore : IProfileStore
{
    public ProfileUpsert? Last; public bool IsNew = true; public UserRole? Prev; public bool Active = true;
    public UpsertOutcome Outcome = UpsertOutcome.Ok;
    public Task<UpsertResult> UpsertAsync(ProfileUpsert p, CancellationToken ct)
    {
        Last = p;
        return Task.FromResult(new UpsertResult(Outcome, Outcome == UpsertOutcome.Ok
            ? new ProfileSnapshot(p.Id, p.Email, p.DisplayName, p.Role, p.EmailVerified, Active, IsNew, Prev) : null));
    }
}

public class IdentityProvisioningTests
{
    private static (IdentityProvisioningService svc, FakeProfileStore store, FakeAudit audit) Make()
    {
        var store = new FakeProfileStore(); var audit = new FakeAudit();
        var svc = new IdentityProvisioningService(new RoleProvisioner(["admin@gmail.com"], ["scorer@gmail.com"], []),
            new EmailDomainPolicy(["gmail.com", "googlemail.com"]), store, audit);
        return (svc, store, audit);
    }
    private static IdentityClaims C(string? email, bool verified = true) => new(Guid.NewGuid(), email, verified, "Name", null, "g-1");

    [Fact]
    public async Task Allowlisted_verified_admin_gets_admin_others_get_captain()
    {
        var (svc, store, _) = Make();
        var r = await svc.ProvisionAsync(C("Admin@gmail.com"), "1.1.1.1", "c", default);
        Assert.Equal(UserRole.Admin, r.Profile!.Role);
        Assert.Equal("allowlist:admin", store.Last!.RoleSource);
        Assert.Equal(UserRole.Captain, (await svc.ProvisionAsync(C("student@gmail.com"), null, null, default)).Profile!.Role);
    }

    [Fact]
    public async Task Unverified_email_never_receives_an_elevated_role()
    {
        var (svc, store, _) = Make();
        var r = await svc.ProvisionAsync(C("admin@gmail.com", verified: false), null, null, default);
        Assert.Equal(UserRole.Captain, r.Profile!.Role);
        Assert.Equal("unverified:captain", store.Last!.RoleSource);
        Assert.False(store.Last.EmailVerified);
    }

    [Fact]
    public async Task Disallowed_domain_and_missing_email_are_rejected_without_creating_a_profile()
    {
        var (svc, store, _) = Make();
        Assert.Equal(ProvisionOutcome.DomainNotAllowed, (await svc.ProvisionAsync(C("x@evil.com"), null, null, default)).Outcome);
        Assert.Equal(ProvisionOutcome.NoEmail, (await svc.ProvisionAsync(C(null), null, null, default)).Outcome);
        Assert.Null(store.Last);
    }

    [Fact]
    public async Task First_sign_in_and_role_changes_are_audited_but_plain_sign_in_is_not()
    {
        var (svc, store, audit) = Make();
        await svc.ProvisionAsync(C("student@gmail.com"), null, null, default);
        Assert.Equal("auth.profile_created", audit.Entries[0].Action);

        store.IsNew = false; store.Prev = UserRole.Captain;
        await svc.ProvisionAsync(C("student@gmail.com"), null, null, default);
        Assert.Equal(1, audit.Entries.Count);

        store.Prev = UserRole.Admin; // demoted because removed from allowlist
        await svc.ProvisionAsync(C("student@gmail.com"), null, null, default);
        Assert.Equal("auth.role_changed", audit.Entries[1].Action);
        Assert.Contains("\"from\":\"Admin\"", audit.Entries[1].MetadataJson);
    }

    [Fact]
    public async Task Inactive_accounts_and_alias_conflicts_are_blocked()
    {
        var (svc, store, _) = Make();
        store.Active = false;
        Assert.Equal(ProvisionOutcome.Inactive, (await svc.ProvisionAsync(C("student@gmail.com"), null, null, default)).Outcome);
        store.Outcome = UpsertOutcome.EmailBelongsToAnotherAccount;
        Assert.Equal(ProvisionOutcome.AccountConflict, (await svc.ProvisionAsync(C("student@gmail.com"), null, null, default)).Outcome);
    }
}
