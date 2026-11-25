namespace SportsWeek.Application.Abstractions;

public enum ResultKind { Ok, NotFound, Invalid, Forbidden, Conflict, BusinessRule }

public sealed class ServiceResult<T>
{
    public ResultKind Kind { get; }
    public T? Value { get; }
    public string? Code { get; }
    public string? Message { get; }
    public IReadOnlyDictionary<string, string[]>? FieldErrors { get; }

    private ServiceResult(ResultKind kind, T? value, string? code, string? message, IReadOnlyDictionary<string, string[]>? fe)
    { Kind = kind; Value = value; Code = code; Message = message; FieldErrors = fe; }

    public bool IsOk => Kind == ResultKind.Ok;
    public static ServiceResult<T> Ok(T value) => new(ResultKind.Ok, value, null, null, null);
    public static ServiceResult<T> NotFound(string msg = "Not found.") => new(ResultKind.NotFound, default, "not_found", msg, null);
    public static ServiceResult<T> Forbidden(string msg = "Forbidden.") => new(ResultKind.Forbidden, default, "forbidden", msg, null);
    public static ServiceResult<T> Conflict(string code, string msg) => new(ResultKind.Conflict, default, code, msg, null);
    public static ServiceResult<T> Rule(string code, string msg) => new(ResultKind.BusinessRule, default, code, msg, null);
    public static ServiceResult<T> Invalid(IReadOnlyDictionary<string, string[]> fe) =>
        new(ResultKind.Invalid, default, "validation_failed", "One or more fields are invalid.", fe);
}
