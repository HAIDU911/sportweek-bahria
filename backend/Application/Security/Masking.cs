using System.Text;

namespace SportsWeek.Application.Security;

/// <summary>Default-masked PII for admin lists. Full values require an explicit, audited reveal.</summary>
public static class Masking
{
    public static string Email(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return string.Empty;
        var at = email.LastIndexOf('@');
        if (at <= 0) return "***";
        var local = email[..at];
        var keep = local.Length <= 2 ? 1 : 2;
        return $"{local[..keep]}***{email[at..]}";
    }

    public static string Enrollment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var v = value.Trim();
        return v.Length <= 4 ? "****" : $"****-{v[^4..]}";
    }
}

/// <summary>RFC 4180 CSV with spreadsheet-formula-injection protection (OWASP CSV Injection).</summary>
public static class CsvBuilder
{
    public static string Build(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(',', headers.Select(Escape))).Append("\r\n");
        foreach (var row in rows) sb.Append(string.Join(',', row.Select(Escape))).Append("\r\n");
        return sb.ToString();
    }

    public static string Escape(string? value)
    {
        var v = value ?? string.Empty;
        if (v.Length > 0 && (v[0] is '=' or '+' or '-' or '@' or '\t' or '\r'))
            v = "'" + v;
        if (v.Contains('"') || v.Contains(',') || v.Contains('\n') || v.Contains('\r'))
            v = "\"" + v.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        return v;
    }
}
