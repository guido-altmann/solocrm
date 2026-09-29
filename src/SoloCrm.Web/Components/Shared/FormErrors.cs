using SoloCrm.Application.Abstractions;

namespace SoloCrm.Web.Components.Shared;

/// <summary>
/// Maps a failed <see cref="Result{T}"/> to field errors (shown at the input) or a general error (shown as alert).
/// </summary>
public sealed class FormErrors
{
    private IReadOnlyDictionary<string, string[]> _fields = new Dictionary<string, string[]>();

    public string? General { get; private set; }

    public void Clear()
    {
        _fields = new Dictionary<string, string[]>();
        General = null;
    }

    /// <param name="fieldOf">Assigns known business errors to a field; <c>null</c> shows them as general error.</param>
    public void Show(Error error, Func<Error, string?>? fieldOf = null)
    {
        ArgumentNullException.ThrowIfNull(error);

        Clear();
        if (error is ValidationError validation)
        {
            _fields = validation.Errors;
        }
        else if (fieldOf?.Invoke(error) is { } field)
        {
            _fields = new Dictionary<string, string[]> { [field] = [error.Message] };
        }
        else
        {
            General = error.Message;
        }
    }

    public bool Has(string field) => _fields.ContainsKey(field);

    public string? Text(string field) =>
        _fields.TryGetValue(field, out var messages) ? string.Join(" ", messages) : null;
}
