using SoloCrm.Domain.Common;

namespace SoloCrm.Domain.Settings;

/// <summary>
/// User-facing setting stored as a JSON value (SPEC 2.4); read typed via <c>IAppSettings</c>.
/// </summary>
public sealed class AppSetting : Entity
{
    public const int KeyMaxLength = 100;

    // Required by EF Core.
    private AppSetting()
    {
        Key = null!;
        Value = null!;
    }

    public AppSetting(string key, string jsonValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonValue);
        Key = key;
        Value = jsonValue;
    }

    public string Key { get; private set; }

    /// <summary>JSON document (jsonb).</summary>
    public string Value { get; private set; }

    public void SetValue(string jsonValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonValue);
        Value = jsonValue;
    }
}
