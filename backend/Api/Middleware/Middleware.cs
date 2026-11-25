using System.Text.RegularExpressions;
using Npgsql;
using SportsWeek.Contracts;
using SportsWeek.Domain.Common;

namespace SportsWeek.Api.Middleware;

public sealed partial class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    [GeneratedRegex("^[A-Za-z0-9\\-_]{8,64}$")]
    private static partial Regex Safe();

    public async Task InvokeAsync(HttpContext ctx)
    {
        var incoming = ctx.Request.Headers["X-Correlation-Id"].ToString();
        var id = Safe().IsMatch(incoming) ? incoming : Guid.NewGuid().ToString("N"); // never reflect arbitrary header content
        ctx.Items["CorrelationId"] = id;
        ctx.Response.Headers["X-Correlation-Id"] = id;
        using (ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("req").BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
            await _next(ctx);
    }
}

public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext ctx)
    {
        ctx.Response.OnStarting(() =>
        {
            var h = ctx.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["X-Frame-Options"] = "DENY";
            h["Referrer-Policy"] = "no-referrer";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            h["Cross-Origin-Resource-Policy"] = "same-site";
            if (!ctx.Request.Path.StartsWithSegments("/swagger"))
                h["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
            // Private data is never cached by browsers or shared caches.
            if (!ctx.Request.Path.StartsWithSegments("/api/public") && !h.ContainsKey("Cache-Control"))
                h["Cache-Control"] = "no-store";
            return Task.CompletedTask;
        });
        return _next(ctx);
    }
}

/// <summary>Converts every unhandled exception into the stable error contract. Never returns stack traces or SQL.</summary>
public sealed class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _log;
    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> log) { _next = next; _log = log; }

    public async Task InvokeAsync(HttpContext ctx)
    {
        try { await _next(ctx); }
        catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested) { ctx.Response.StatusCode = 499; }
        catch (Exception ex)
        {
            var corr = ctx.Items["CorrelationId"] as string;
            var (status, code, msg) = ex switch
            {
                DomainException d => (422, d.Code, d.Message),
                BadHttpRequestException => (400, "bad_request", "The request was malformed."),
                NpgsqlException or TimeoutException => (503, "dependency_unavailable", "A required service is temporarily unavailable."),
                _ => (500, "internal_error", "Something went wrong. Quote the correlation id when contacting support.")
            };
            _log.LogError(ex, "Unhandled {Status} {Code} corr={Corr}", status, code, corr); // details stay in server logs only
            if (ctx.Response.HasStarted) throw;
            ctx.Response.Clear();
            ctx.Response.StatusCode = status;
            await ctx.Response.WriteAsJsonAsync(new ErrorResponse(code, msg, null, corr));
        }
    }
}
