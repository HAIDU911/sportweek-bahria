using Microsoft.AspNetCore.Authorization;
using SportsWeek.Api.Auth;
using SportsWeek.Application.Abstractions;
using SportsWeek.Application.Services;
using SportsWeek.Contracts;

namespace SportsWeek.Api.Endpoints;

internal static class ResultMapper
{
    public static IResult ToHttp<T>(this ServiceResult<T> r, HttpContext ctx)
    {
        var corr = ctx.Items["CorrelationId"] as string;
        return r.Kind switch
        {
            ResultKind.Ok => Results.Ok(r.Value),
            ResultKind.NotFound => Results.Json(new ErrorResponse(r.Code!, r.Message!, null, corr), statusCode: 404),
            ResultKind.Forbidden => Results.Json(new ErrorResponse(r.Code!, r.Message!, null, corr), statusCode: 403),
            ResultKind.Invalid => Results.Json(new ErrorResponse(r.Code!, r.Message!, r.FieldErrors, corr), statusCode: 400),
            ResultKind.Conflict => Results.Json(new ErrorResponse(r.Code!, r.Message!, null, corr), statusCode: 409),
            _ => Results.Json(new ErrorResponse(r.Code!, r.Message!, null, corr), statusCode: 422)
        };
    }
}

public sealed record ExportRequest(string? Search, string? Role, bool? Verified, Guid? TournamentId, Guid? SportId,
    string? Department, string? TeamStatus, bool IncludeSensitive, string? Reason);

public static class EndpointMap
{
    public static void MapSportsWeek(this WebApplication app)
    {
        MapPublic(app); MapMe(app); MapCaptain(app); MapOps(app); MapAdmin(app);
    }

    private static void MapPublic(WebApplication app)
    {
        var g = app.MapGroup("/api/public").RequireRateLimiting("public")
            .AddEndpointFilter(async (ctx, next) =>
            {
                ctx.HttpContext.Response.Headers.CacheControl = "public, max-age=5"; // visitor-independent live data; matches the 5 s auto-refresh
                return await next(ctx);
            });

        g.MapGet("/sports", (IPublicReadStore s, CancellationToken ct) => s.ListSportsAsync(ct));
        g.MapGet("/tournaments", async (IPublicReadStore s, int? page, int? pageSize, string? status, CancellationToken ct) =>
        {
            var (p, size) = Paging.Normalize(page ?? 1, pageSize ?? 25);
            return Results.Ok(await s.ListTournamentsAsync(p, size, string.IsNullOrWhiteSpace(status) ? null : status, ct));
        });
        g.MapGet("/tournaments/{id:guid}", async (Guid id, IPublicReadStore s, HttpContext ctx, CancellationToken ct) =>
            await s.GetTournamentAsync(id, ct) is { } t ? Results.Ok(t)
                : Results.Json(new ErrorResponse("not_found", "Tournament not found.", null, ctx.Items["CorrelationId"] as string), statusCode: 404));
        g.MapGet("/tournaments/{id:guid}/fixtures", async (Guid id, IPublicReadStore s, int? page, int? pageSize, CancellationToken ct) =>
        {
            var (p, size) = Paging.Normalize(page ?? 1, pageSize ?? 50);
            return Results.Ok(await s.ListFixturesAsync(id, p, size, ct));
        });
        g.MapGet("/teams", async (IPublicReadStore s, string? search, int? page, int? pageSize, CancellationToken ct) =>
        {
            var (p, size) = Paging.Normalize(page ?? 1, pageSize ?? 25);
            return Results.Ok(await s.ListTeamsAsync(string.IsNullOrWhiteSpace(search) ? null : search.Trim(), p, size, ct));
        });
        g.MapGet("/home", (IPublicReadStore s, CancellationToken ct) => s.GetHomeAsync(ct));
        g.MapGet("/fixtures", async (IPublicReadStore s, string? status, Guid? sportId, Guid? tournamentId, DateTime? day, int? page, int? pageSize, CancellationToken ct) =>
        {
            var (p, size) = Paging.Normalize(page ?? 1, pageSize ?? 50);
            return Results.Ok(await s.ListFeedAsync(new FeedQuery(string.IsNullOrWhiteSpace(status) ? null : status, sportId, tournamentId, day, p, size), ct));
        });
        g.MapGet("/tournaments/{id:guid}/standings", (Guid id, IPublicReadStore s, CancellationToken ct) => s.GetStandingsAsync(id, ct));
        g.MapGet("/leaderboards/overall", (IPublicReadStore s, CancellationToken ct) => s.GetOverallLeaderboardAsync(ct));
    }

    private static void MapMe(WebApplication app) =>
        app.MapGet("/api/me", (HttpContext ctx) =>
        {
            var u = ctx.User;
            if (u.FindFirst(Claims.Reject) is { } rej)
                return Results.Json(new ErrorResponse("identity.rejected", $"Sign-in not permitted ({rej.Value}).", null, ctx.Items["CorrelationId"] as string), statusCode: 403);
            var a = u.ToActor(ctx);
            return Results.Ok(new CurrentUserDto(a.UserId, a.Email, u.FindFirst(Claims.DisplayName)?.Value ?? a.Email, a.Role.ToString(), a.EmailVerified));
        }).RequireAuthorization().RequireRateLimiting("authenticated");

    private static void MapCaptain(WebApplication app)
    {
        var g = app.MapGroup("/api/captain").RequireAuthorization("Captain").RequireRateLimiting("authenticated");

        g.MapGet("/teams", async (HttpContext ctx, CaptainService s, CancellationToken ct) =>
            (await s.ListOwnTeamsAsync(ctx.User.ToActor(ctx), ct)).ToHttp(ctx));

        g.MapGet("/notifications", async (HttpContext ctx, ICaptainExtrasStore s, CancellationToken ct) =>
            Results.Ok(await s.ListNotificationsAsync(ctx.User.ToActor(ctx).UserId, 50, ct)));
        g.MapPost("/notifications/read-all", async (HttpContext ctx, ICaptainExtrasStore s, CancellationToken ct) =>
            Results.Ok(new { marked = await s.MarkAllReadAsync(ctx.User.ToActor(ctx).UserId, ct) }));
        g.MapGet("/fixtures", async (HttpContext ctx, ICaptainExtrasStore s, CancellationToken ct) =>
            Results.Ok(await s.ListFixturesForOwnerAsync(ctx.User.ToActor(ctx).UserId, ct)));
        g.MapPost("/teams/{id:guid}/withdraw", async (Guid id, WithdrawRequest b, HttpContext ctx, TournamentOpsService s, CancellationToken ct) =>
            (await s.WithdrawAsync(ctx.User.ToActor(ctx), id, b.Reason, ct)).ToHttp(ctx));

        g.MapPost("/tournaments/{id:guid}/teams", async (Guid id, RegisterTeamRequest body, HttpContext ctx, CaptainService s, CancellationToken ct) =>
        {
            var key = ctx.Request.Headers["Idempotency-Key"].ToString();
            var r = await s.RegisterTeamAsync(ctx.User.ToActor(ctx), id, body, string.IsNullOrEmpty(key) ? null : key, ct);
            return r.IsOk ? Results.Json(new { teamId = r.Value }, statusCode: 201) : r.ToHttp(ctx);
        });
    }

    // ===== OPERATOR DESK (Scorer role). The read-only Admin cannot reach these. =====
    private static void MapOps(WebApplication app)
    {
        var g = app.MapGroup("/api/ops").RequireAuthorization("Scorer").RequireRateLimiting("authenticated");
        static Actor A(HttpContext c) => c.User.ToActor(c);

        g.MapGet("/tournaments", async (HttpContext c, TournamentOpsService s, CancellationToken ct) => (await s.ListTournamentsAsync(A(c), ct)).ToHttp(c));
        g.MapPost("/tournaments", async (CreateTournamentRequest b, HttpContext c, TournamentOpsService s, CancellationToken ct) =>
        {
            var r = await s.CreateTournamentAsync(A(c), b, ct);
            return r.IsOk ? Results.Json(new { id = r.Value }, statusCode: 201) : r.ToHttp(c);
        });
        g.MapPost("/tournaments/{id:guid}/status", async (Guid id, TournamentStatusRequest b, HttpContext c, TournamentOpsService s, CancellationToken ct) =>
            (await s.SetStatusAsync(A(c), id, b.Status, ct)).ToHttp(c));
        g.MapPost("/tournaments/{id:guid}/fixtures/generate", async (Guid id, HttpContext c, TournamentOpsService s, CancellationToken ct) =>
            (await s.GenerateFixturesAsync(A(c), id, ct)).ToHttp(c));
        g.MapGet("/tournaments/{id:guid}/fixtures", async (Guid id, HttpContext c, TournamentOpsService s, CancellationToken ct) =>
            (await s.ListFixturesAsync(A(c), id, ct)).ToHttp(c));
        g.MapPost("/tournaments/{id:guid}/regeneration/preview", async (Guid id, HttpContext c, TournamentOpsService s, CancellationToken ct) =>
            (await s.PreviewRegenerationAsync(A(c), id, ct)).ToHttp(c));
        g.MapPost("/tournaments/{id:guid}/regeneration/confirm", async (Guid id, HttpContext c, TournamentOpsService s, CancellationToken ct) =>
            (await s.ConfirmRegenerationAsync(A(c), id, ct)).ToHttp(c));

        g.MapGet("/teams", async (HttpContext c, TournamentOpsService s, string? search, Guid? tournamentId, string? status, int? page, int? pageSize, CancellationToken ct) =>
            (await s.ListTeamsAsync(A(c), new TeamQuery(search, tournamentId, status, page ?? 1, pageSize ?? 25), ct)).ToHttp(c));
        g.MapGet("/teams/{id:guid}", async (Guid id, HttpContext c, TournamentOpsService s, CancellationToken ct) => (await s.GetTeamAsync(A(c), id, ct)).ToHttp(c));
        g.MapPost("/teams/{id:guid}/review", async (Guid id, ReviewTeamRequest b, HttpContext c, TournamentOpsService s, CancellationToken ct) =>
            (await s.ReviewTeamAsync(A(c), id, b, ct)).ToHttp(c));
        g.MapPost("/teams/{id:guid}/withdraw", async (Guid id, WithdrawRequest b, HttpContext c, TournamentOpsService s, CancellationToken ct) =>
            (await s.WithdrawAsync(A(c), id, b.Reason, ct)).ToHttp(c));

        g.MapPost("/fixtures/{id:guid}/schedule", async (Guid id, ScheduleRequest b, HttpContext c, TournamentOpsService s, CancellationToken ct) =>
            (await s.ScheduleAsync(A(c), id, b, ct)).ToHttp(c));
        g.MapPost("/fixtures/{id:guid}/start", async (Guid id, StartRequest b, HttpContext c, TournamentOpsService s, CancellationToken ct) =>
            (await s.StartAsync(A(c), id, b.Version, ct)).ToHttp(c));
        g.MapPost("/fixtures/{id:guid}/live", async (Guid id, LiveDetailRequest b, HttpContext c, TournamentOpsService s, CancellationToken ct) =>
            (await s.SetLiveDetailAsync(A(c), id, b, ct)).ToHttp(c));
        g.MapPost("/fixtures/{id:guid}/result", async (Guid id, ResultRequest b, HttpContext c, TournamentOpsService s, CancellationToken ct) =>
            (await s.RecordResultAsync(A(c), id, b, ct)).ToHttp(c));
    }

    // ===== READ-ONLY ADMIN CONSOLE: only GET endpoints for data, plus audited POST exports. No write path to users/teams exists. =====
    private static void MapAdmin(WebApplication app)
    {
        var g = app.MapGroup("/api/admin").RequireAuthorization("Admin").RequireRateLimiting("authenticated");

        g.MapGet("/dashboard", async (HttpContext ctx, AdminDirectoryService s, CancellationToken ct) =>
            (await s.GetDashboardAsync(ctx.User.ToActor(ctx), ct)).ToHttp(ctx));

        g.MapGet("/users", async (HttpContext ctx, AdminDirectoryService s, string? search, string? role, bool? verified,
            DateTime? createdFrom, DateTime? createdTo, int? page, int? pageSize, CancellationToken ct) =>
            (await s.ListUsersAsync(ctx.User.ToActor(ctx),
                new UserQuery(search, role, verified, createdFrom, createdTo, page ?? 1, pageSize ?? 25), ct)).ToHttp(ctx));

        g.MapGet("/users/{id:guid}", async (Guid id, bool? reveal, HttpContext ctx, AdminDirectoryService s, CancellationToken ct) =>
            (await s.GetUserAsync(ctx.User.ToActor(ctx), id, reveal ?? false, ct)).ToHttp(ctx));

        g.MapGet("/participants", async (HttpContext ctx, AdminDirectoryService s, string? search, Guid? tournamentId, Guid? sportId,
            string? department, string? teamStatus, int? page, int? pageSize, CancellationToken ct) =>
            (await s.ListParticipantsAsync(ctx.User.ToActor(ctx),
                new ParticipantQuery(search, tournamentId, sportId, department, teamStatus, page ?? 1, pageSize ?? 25), ct)).ToHttp(ctx));

        g.MapGet("/teams", async (HttpContext ctx, AdminDirectoryService s, string? search, Guid? tournamentId, string? status,
            int? page, int? pageSize, CancellationToken ct) =>
            (await s.ListTeamsAsync(ctx.User.ToActor(ctx), new TeamQuery(search, tournamentId, status, page ?? 1, pageSize ?? 25), ct)).ToHttp(ctx));

        g.MapGet("/teams/{id:guid}", async (Guid id, HttpContext ctx, AdminDirectoryService s, CancellationToken ct) =>
            (await s.GetTeamAsync(ctx.User.ToActor(ctx), id, ct)).ToHttp(ctx));

        g.MapGet("/departments", async (HttpContext ctx, AdminDirectoryService s, CancellationToken ct) =>
            (await s.GetDepartmentsAsync(ctx.User.ToActor(ctx), ct)).ToHttp(ctx));

        g.MapGet("/audit", async (HttpContext ctx, AdminDirectoryService s, string? action, Guid? actorId, string? entityType,
            DateTime? from, DateTime? to, int? page, int? pageSize, CancellationToken ct) =>
            (await s.ListAuditAsync(ctx.User.ToActor(ctx), new AuditQuery(action, actorId, entityType, from, to, page ?? 1, pageSize ?? 50), ct)).ToHttp(ctx));

        var ex = g.MapGroup("/exports").RequireRateLimiting("export");
        ex.MapPost("/users", async (ExportRequest b, HttpContext ctx, AdminDirectoryService s, CancellationToken ct) =>
        {
            var r = await s.ExportUsersAsync(ctx.User.ToActor(ctx), new UserQuery(b.Search, b.Role, b.Verified, null, null), b.IncludeSensitive, b.Reason, ct);
            return r.IsOk ? Results.File(System.Text.Encoding.UTF8.GetBytes(r.Value!.Csv), "text/csv; charset=utf-8", r.Value.FileName) : r.ToHttp(ctx);
        });
        ex.MapPost("/participants", async (ExportRequest b, HttpContext ctx, AdminDirectoryService s, CancellationToken ct) =>
        {
            var r = await s.ExportParticipantsAsync(ctx.User.ToActor(ctx),
                new ParticipantQuery(b.Search, b.TournamentId, b.SportId, b.Department, b.TeamStatus), b.IncludeSensitive, b.Reason, ct);
            return r.IsOk ? Results.File(System.Text.Encoding.UTF8.GetBytes(r.Value!.Csv), "text/csv; charset=utf-8", r.Value.FileName) : r.ToHttp(ctx);
        });
    }
}
