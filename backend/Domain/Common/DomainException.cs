namespace SportsWeek.Domain.Common;

/// <summary>Raised when a domain invariant would be violated. Maps to HTTP 422 at the API edge.</summary>
public sealed class DomainException : Exception
{
    public string Code { get; }

    public DomainException(string code, string message) : base(message) => Code = code;
}
