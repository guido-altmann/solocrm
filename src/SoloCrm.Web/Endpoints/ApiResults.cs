using System.Text.Json;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Web.Endpoints;

/// <summary>
/// Maps <see cref="Result{T}"/> errors to RFC 9457 Problem Details (SPEC 5): validation → 400 with camelCase field
/// names, not found → 404, conflict → 409, other business rules → 422. The <c>code</c> extension carries the error code.
/// </summary>
internal static class ApiResults
{
    /// <summary>Command properties whose API field has another name.</summary>
    private static readonly Dictionary<string, string> FieldNames = new(StringComparer.Ordinal)
    {
        ["NewOrganizationName"] = "organizationName",
        ["PageIndex"] = "page",
        ["TagIds"] = "tag",
    };

    public static IResult Problem(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (error is ValidationError validation)
        {
            return ValidationProblem(validation.Errors);
        }

        var (status, title) = error.Type switch
        {
            ErrorType.NotFound => (StatusCodes.Status404NotFound, "Nicht gefunden"),
            ErrorType.Conflict => (StatusCodes.Status409Conflict, "Konflikt"),
            _ => (StatusCodes.Status422UnprocessableEntity, "Fachlicher Fehler"),
        };

        return TypedResults.Problem(
            statusCode: status,
            title: title,
            detail: error.Message,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }

    public static IResult ValidationProblem(IReadOnlyDictionary<string, string[]> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        var fields = errors
            .GroupBy(e => FieldName(e.Key), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.SelectMany(e => e.Value).Distinct().ToArray(), StringComparer.Ordinal);
        return TypedResults.ValidationProblem(fields, title: "Die Eingaben sind ungültig.");
    }

    public static IResult NotFound(string detail) =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Nicht gefunden", detail: detail);

    private static string FieldName(string property) =>
        FieldNames.TryGetValue(property, out var name) ? name : JsonNamingPolicy.CamelCase.ConvertName(property);
}
