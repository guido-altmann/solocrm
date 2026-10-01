using System.Text.Json;
using System.Text.Json.Nodes;

namespace SoloCrm.Web.Endpoints;

/// <summary>
/// JSON Merge Patch (RFC 7396, iteration 5 decision 4) over a flat resource: a missing field keeps the current value,
/// <c>null</c> clears it. Unknown fields and values of the wrong type are collected as validation errors.
/// </summary>
internal sealed class MergePatch
{
    public const string ContentType = "application/merge-patch+json";

    private readonly JsonObject _body;
    private readonly JsonSerializerOptions _options;
    private readonly Dictionary<string, string[]> _errors = new(StringComparer.Ordinal);

    private MergePatch(JsonObject body, JsonSerializerOptions options)
    {
        _body = body;
        _options = options;
    }

    public bool IsValid => _errors.Count == 0;

    public static MergePatch Parse(JsonObject? body, IReadOnlyCollection<string> fields, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var patch = new MergePatch(body ?? [], options);
        foreach (var (name, _) in patch._body)
        {
            if (!fields.Contains(name))
            {
                patch._errors[name] = ["Unbekanntes Feld."];
            }
        }

        return patch;
    }

    public bool Has(string field) => _body.ContainsKey(field);

    /// <summary>The patched value of <paramref name="field"/>, or <paramref name="current"/> when it is not sent.</summary>
    public T Value<T>(string field, T current)
    {
        if (!_body.TryGetPropertyValue(field, out var node))
        {
            return current;
        }

        if (node is null)
        {
            if (typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) is null)
            {
                _errors[field] = ["Darf nicht leer sein."];
                return current;
            }

            return default!;
        }

        try
        {
            return node.Deserialize<T>(_options)!;
        }
        catch (JsonException)
        {
            _errors[field] = ["Ungültiger Wert."];
            return current;
        }
    }

    public IResult ValidationProblem() => ApiResults.ValidationProblem(_errors);
}
