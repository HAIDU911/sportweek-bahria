using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using SportsWeek.Application.Abstractions;

namespace SportsWeek.Infrastructure.Data;

public static class DapperConfig
{
    private static int _initialised;

    /// <summary>snake_case columns ↔ PascalCase record parameters. Call once at startup.</summary>
    public static void Init()
    {
        if (Interlocked.Exchange(ref _initialised, 1) == 1) return;
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }
}

internal static class Q
{
    /// <summary>Escapes LIKE wildcards so user input is matched literally (paired with `escape '\'` in SQL).</summary>
    public static string? Pattern(string? search) =>
        string.IsNullOrWhiteSpace(search)
            ? null
            : "%" + search.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal)
                          .Replace("_", "\\_", StringComparison.Ordinal) + "%";

    public static DateTime? Utc(DateTime? d) => d is null ? null : DateTime.SpecifyKind(d.Value.ToUniversalTime(), DateTimeKind.Utc);

    public static CommandDefinition Cmd(string sql, object? p, CancellationToken ct, NpgsqlTransaction? tx = null) =>
        new(sql, p, tx, commandTimeout: 15, cancellationToken: ct);
}

/// <summary>Append-only audit writer. Throws on failure so sensitive reads fail CLOSED when they cannot be recorded.</summary>
public sealed class AuditWriter : IAuditWriter
{
    private readonly NpgsqlDataSource _ds;
    public AuditWriter(NpgsqlDataSource ds) => _ds = ds;

    public async Task WriteAsync(AuditEntry e, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(e);
        await using var conn = await _ds.OpenConnectionAsync(ct);
        string? meta = e.MetadataJson;
        if (meta is not null)
        {
            try { using var _ = JsonDocument.Parse(meta); } // never store malformed JSON
            catch (JsonException) { meta = JsonSerializer.Serialize(new { raw = "invalid-metadata-omitted" }); }
        }
        await conn.ExecuteAsync(Q.Cmd(Sql.AuditInsert, new
        {
            action = e.Action, actor = e.ActorId, entityType = e.EntityType, entityId = e.EntityId,
            result = e.Result, correlationId = e.CorrelationId, ip = e.IpAddress, metadata = meta
        }, ct));
    }
}

public sealed class DbHealthCheck : IHealthCheck
{
    private readonly NpgsqlDataSource _ds;
    public DbHealthCheck(NpgsqlDataSource ds) => _ds = ds;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            await using var conn = await _ds.OpenConnectionAsync(cts.Token);
            await conn.ExecuteScalarAsync<int>(new CommandDefinition("select 1", cancellationToken: cts.Token));
            return HealthCheckResult.Healthy();
        }
        catch (Exception) // never leak the exception (connection strings, hosts) to the caller
        {
            return HealthCheckResult.Unhealthy("database unavailable");
        }
    }
}
