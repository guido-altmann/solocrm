using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace SoloCrm.Infrastructure.Persistence;

/// <summary>
/// Value converters and comparers for values stored as jsonb. The JSON is produced explicitly with
/// System.Text.Json so the stored shape does not depend on provider-specific POCO mapping.
/// </summary>
internal static class JsonColumn
{
    public const string ColumnType = "jsonb";

    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static ValueConverter<IReadOnlyDictionary<string, string>, string> DictionaryConverter { get; } = new(
        v => JsonSerializer.Serialize(v, Options),
        v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, Options) ?? new Dictionary<string, string>());

    public static ValueComparer<IReadOnlyDictionary<string, string>> DictionaryComparer { get; } = new(
        (a, b) => a!.Count == b!.Count && !a.Except(b).Any(),
        v => v.Aggregate(0, (hash, pair) => HashCode.Combine(hash, pair.Key, pair.Value)),
        v => new Dictionary<string, string>(v));

    public static ValueConverter<IReadOnlyList<T>, string> ListConverter<T>() => new(
        v => JsonSerializer.Serialize(v, Options),
        v => JsonSerializer.Deserialize<List<T>>(v, Options) ?? new List<T>());

    public static ValueComparer<IReadOnlyList<T>> ListComparer<T>() => new(
        (a, b) => a!.SequenceEqual(b!),
        v => v.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
        v => v.ToList());
}
