using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using SportsWeek.Api.Auth;
using SportsWeek.Api.Endpoints;
using SportsWeek.Api.Middleware;
using SportsWeek.Application.Abstractions;
using SportsWeek.Application.Security;
using SportsWeek.Application.Services;
using SportsWeek.Contracts;
using SportsWeek.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;
var isDev = builder.Environment.IsDevelopment();

// ---------- fail fast on missing security-critical configuration ----------
string Require(string key) =>
    cfg[key] is { Length: > 0 } v ? v : throw new InvalidOperationException($"Missing required configuration '{key}'. See .env.example.");

var supabaseUrl = Require("Supabase:Url").TrimEnd('/');
var connString = Require("ConnectionStrings:Supabase");
var audience = cfg["Supabase:Audience"] ?? "authenticated";
var issuer = supabaseUrl + "/auth/v1";
var legacySecret = cfg["Supabase:JwtSecret"];

DapperConfig.Init();
builder.Services.AddSingleton(NpgsqlDataSource.Create(connString));
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();

// ---------- application services ----------
string[] List(string key) => cfg.GetSection(key).Get<string[]>() ?? [];
builder.Services.AddSingleton(new RoleProvisioner(List("Auth:Admins"), List("Auth:Scorers"), List("Auth:Stakeholders")));
builder.Services.AddSingleton(new EmailDomainPolicy(List("Auth:AllowedDomains")));
builder.Services.AddSingleton<IAuditWriter, AuditWriter>();
builder.Services.AddSingleton<IProfileStore, ProfileStore>();
builder.Services.AddSingleton<IAdminReadStore, AdminReadStore>();
builder.Services.AddSingleton<ICaptainStore, CaptainStore>();
builder.Services.AddSingleton<IPublicReadStore, PublicReadStore>();
builder.Services.AddSingleton<IdentityProvisioningService>();
builder.Services.AddSingleton<IOpsStore, OpsStore>();
builder.Services.AddSingleton<ICaptainExtrasStore, CaptainExtrasStore>();
builder.Services.AddSingleton<AdminDirectoryService>();
builder.Services.AddSingleton<TournamentOpsService>();
builder.Services.AddSingleton<CaptainService>();

// ---------- authentication: Supabase JWT (issuer, audience, signature/JWKS, expiry) ----------
var jwks = new JwksCache(new HttpClient { Timeout = TimeSpan.FromSeconds(5) }, issuer);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.RequireHttpsMetadata = !isDev;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = issuer,
        ValidateAudience = true, ValidAudience = audience,
        ValidateLifetime = true, RequireExpirationTime = true, ClockSkew = TimeSpan.FromSeconds(30),
        ValidateIssuerSigningKey = true, RequireSignedTokens = true,
        ValidAlgorithms = ["ES256", "RS256", "HS256"], // 'none' and anything else is rejected
        IssuerSigningKeyResolver = (_, _, kid, _) =>
            string.IsNullOrEmpty(legacySecret)
                ? jwks.Resolve(kid)
                : [new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(legacySecret))]
    };
});
builder.Services.AddTransient<IClaimsTransformation, ProfileClaimsTransformation>();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Admin", p => p.RequireAuthenticatedUser().RequireClaim(Claims.Role, "Admin"))
    .AddPolicy("Scorer", p => p.RequireAuthenticatedUser().RequireClaim(Claims.Role, "Scorer"))
    .AddPolicy("Captain", p => p.RequireAuthenticatedUser().RequireClaim(Claims.Role, "Captain"));

// ---------- CORS (strict allowlist; bearer tokens, no cookies) ----------
// Content-Disposition must be exposed or browsers hide the CSV filename from the frontend.
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(List("Cors:Origins"))
    .WithMethods("GET", "POST", "OPTIONS")
    .WithHeaders("Authorization", "Content-Type", "Idempotency-Key", "X-Correlation-Id")
    .WithExposedHeaders("X-Correlation-Id", "Retry-After", "Content-Disposition")));

// ---------- rate limiting ----------
static string Who(HttpContext c) => c.User.FindFirst("sub")?.Value ?? c.Connection.RemoteIpAddress?.ToString() ?? "anon";
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("public", c => RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("authenticated", c => RateLimitPartition.GetFixedWindowLimiter(Who(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 240, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("export", c => RateLimitPartition.GetFixedWindowLimiter(Who(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(10) }));
    o.OnRejected = async (ctx, ct) =>
    {
        if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var ra))
            ctx.HttpContext.Response.Headers.RetryAfter = ((int)ra.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await ctx.HttpContext.Response.WriteAsJsonAsync(new ErrorResponse("rate_limited", "Too many requests. Please slow down.", null,
            ctx.HttpContext.Items["CorrelationId"] as string), ct);
    };
});

builder.Services.AddHealthChecks().AddCheck<DbHealthCheck>("database", tags: ["ready"]);
builder.Services.AddSingleton<DbHealthCheck>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
builder.WebHost.ConfigureKestrel(k => { k.AddServerHeader = false; k.Limits.MaxRequestBodySize = 64 * 1024; });
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Trust only your reverse proxy. Add its address/network here in production (see docs/SECURITY.md).
    if (isDev) { o.KnownProxies.Add(IPAddress.Loopback); }
});
if (isDev) { builder.Services.AddEndpointsApiExplorer(); builder.Services.AddSwaggerGen(); }

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
if (!isDev) { app.UseHsts(); app.UseHttpsRedirection(); }
app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

if (isDev) { app.UseSwagger(); app.UseSwaggerUI(); } // Swagger is never exposed outside Development

app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });                      // liveness: no DB dependency
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }); // readiness: 503 if DB down
app.MapSportsWeek();

app.Run();

public partial class Program;
