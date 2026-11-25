using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using SportsWeek.Application.Abstractions;
using SportsWeek.Application.Services;
using SportsWeek.Domain.Enums;

namespace SportsWeek.Api.Auth;

public static class Claims
{
    public const string Role = "sw_role";
    public const string Reject = "sw_reject";
    public const string Verified = "sw_verified";
    public const string DisplayName = "sw_name";
}

/// <summary>Fetches and caches the Supabase JWKS (asymmetric signing keys) and handles key rotation.</summary>
public sealed class JwksCache
{
    private readonly HttpClient _http;
    private readonly string _url;
    private readonly object _lock = new();
    private JsonWebKeySet? _set;
    private DateTime _fetchedUtc = DateTime.MinValue;

    public JwksCache(HttpClient http, string issuer) { _http = http; _url = issuer.TrimEnd('/') + "/.well-known/jwks.json"; }

    public IEnumerable<SecurityKey> Resolve(string? kid)
    {
        lock (_lock)
        {
            var age = DateTime.UtcNow - _fetchedUtc;
            var missing = _set is not null && kid is not null && !_set.Keys.Any(k => k.Kid == kid);
            if (_set is null || age > TimeSpan.FromMinutes(10) || (missing && age > TimeSpan.FromSeconds(30)))
            {
                _set = new JsonWebKeySet(_http.GetStringAsync(_url).GetAwaiter().GetResult());
                _fetchedUtc = DateTime.UtcNow;
            }
            return _set.GetSigningKeys().ToList();
        }
    }
}

/// <summary>
/// After the token signature is validated, derive the caller's ROLE FROM THE DATABASE PROFILE (allowlists) — never from the token.
/// Result is cached briefly per user to avoid a DB write on every request; role changes apply within the cache window.
/// </summary>
public sealed class ProfileClaimsTransformation : IClaimsTransformation
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);
    private readonly IdentityProvisioningService _svc;
    private readonly IMemoryCache _cache;
    private readonly IHttpContextAccessor _http;

    public ProfileClaimsTransformation(IdentityProvisioningService svc, IMemoryCache cache, IHttpContextAccessor http)
    { _svc = svc; _cache = cache; _http = http; }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true || principal.HasClaim(c => c.Type is Claims.Role or Claims.Reject)) return principal;

        var sub = principal.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var id)) return Reject(principal, "identity.invalid_subject");

        var ctx = _http.HttpContext;
        var ip = ctx?.Connection.RemoteIpAddress?.ToString();
        var corr = ctx?.Items["CorrelationId"] as string;

        var result = await _cache.GetOrCreateAsync($"prov:{id}", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = CacheFor;
            var (verified, name, avatar, gsub) = ReadMetadata(principal);
            return await _svc.ProvisionAsync(new IdentityClaims(id, principal.FindFirstValue("email"), verified, name, avatar, gsub),
                ip, corr, ctx?.RequestAborted ?? default);
        });

        if (result is null || result.Outcome != ProvisionOutcome.Ok || result.Profile is null)
            return Reject(principal, result?.Outcome.ToString() ?? "identity.failed");

        var p = result.Profile;
        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim(Claims.Role, p.Role.ToString()));
        identity.AddClaim(new Claim(Claims.Verified, p.EmailVerified ? "true" : "false"));
        identity.AddClaim(new Claim(Claims.DisplayName, p.DisplayName));
        principal.AddIdentity(identity);
        return principal;
    }

    private static ClaimsPrincipal Reject(ClaimsPrincipal p, string why)
    {
        p.AddIdentity(new ClaimsIdentity([new Claim(Claims.Reject, why)]));
        return p;
    }

    private static (bool verified, string? name, string? avatar, string? googleSub) ReadMetadata(ClaimsPrincipal p)
    {
        bool verified = false; string? name = null, avatar = null, gsub = null;
        var meta = p.FindFirstValue("user_metadata");
        if (!string.IsNullOrWhiteSpace(meta))
        {
            try
            {
                using var doc = JsonDocument.Parse(meta);
                var r = doc.RootElement;
                if (r.TryGetProperty("email_verified", out var v) && v.ValueKind == JsonValueKind.True) verified = true;
                if (r.TryGetProperty("full_name", out var n) && n.ValueKind == JsonValueKind.String) name = n.GetString();
                else if (r.TryGetProperty("name", out var n2) && n2.ValueKind == JsonValueKind.String) name = n2.GetString();
                if (r.TryGetProperty("avatar_url", out var a) && a.ValueKind == JsonValueKind.String) avatar = a.GetString();
                if (r.TryGetProperty("provider_id", out var g) && g.ValueKind == JsonValueKind.String) gsub = g.GetString();
            }
            catch (JsonException) { /* treat as unverified */ }
        }
        if (string.Equals(p.FindFirstValue("email_verified"), "true", StringComparison.OrdinalIgnoreCase)) verified = true;
        return (verified, name, avatar, gsub);
    }
}

public static class PrincipalExtensions
{
    public static Actor ToActor(this ClaimsPrincipal user, HttpContext ctx)
    {
        _ = Guid.TryParse(user.FindFirstValue("sub"), out var id);
        _ = Enum.TryParse<UserRole>(user.FindFirstValue(Claims.Role), out var role);
        return new Actor(id, user.FindFirstValue("email") ?? string.Empty, role,
            user.FindFirstValue(Claims.Verified) == "true", ctx.Connection.RemoteIpAddress?.ToString(), ctx.Items["CorrelationId"] as string);
    }
}
