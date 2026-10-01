using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.ApiKeys;

public static class ApiKeyErrors
{
    public static Error NotFound { get; } =
        Error.NotFound("ApiKey.NotFound", "Der API-Key wurde nicht gefunden.");

    /// <summary>Deliberately the same for unknown, revoked and malformed keys.</summary>
    public static Error Invalid { get; } =
        Error.Failure("ApiKey.Invalid", "Der API-Key ist ungültig oder widerrufen.");
}
