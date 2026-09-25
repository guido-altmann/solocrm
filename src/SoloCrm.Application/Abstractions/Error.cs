namespace SoloCrm.Application.Abstractions;

public enum ErrorType
{
    Failure,
    Validation,
    NotFound,
    Conflict,
}

/// <summary>
/// A business error returned via <see cref="Result{T}"/> instead of an exception.
/// <see cref="Message"/> is user-facing and therefore German.
/// </summary>
public record Error(string Code, string Message, ErrorType Type = ErrorType.Failure)
{
    public static Error Failure(string code, string message) => new(code, message);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
}

/// <summary>
/// Validation failure with messages per property name.
/// </summary>
public sealed record ValidationError(IReadOnlyDictionary<string, string[]> Errors)
    : Error("Validation", "Die Eingaben sind ungültig.", ErrorType.Validation);
