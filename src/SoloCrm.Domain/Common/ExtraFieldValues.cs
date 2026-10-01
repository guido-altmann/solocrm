namespace SoloCrm.Domain.Common;

/// <summary>Changes to <see cref="IHasExtraFields.ExtraFields"/> (SPEC 2.2).</summary>
public static class ExtraFieldValues
{
    /// <summary>
    /// A copy with the trimmed value set, or the key removed for a blank value. Entities assign the copy, so the change
    /// tracker sees the jsonb value as modified.
    /// </summary>
    public static IReadOnlyDictionary<string, string> With(IReadOnlyDictionary<string, string> fields, string key, string? value)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var copy = new Dictionary<string, string>(fields, StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(value))
        {
            copy.Remove(key);
        }
        else
        {
            copy[key] = value.Trim();
        }

        return copy;
    }
}
