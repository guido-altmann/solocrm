using SoloCrm.Domain.Opportunities;

namespace SoloCrm.Application.Abstractions;

/// <summary>
/// Typed access to user-facing settings (SPEC 2.4). Missing or unreadable values fall back to the key's default.
/// </summary>
public interface IAppSettings
{
    Task<T> GetAsync<T>(AppSettingKey<T> key, CancellationToken cancellationToken);

    Task SetAsync<T>(AppSettingKey<T> key, T value, CancellationToken cancellationToken);
}

public sealed record AppSettingKey<T>(string Name, T DefaultValue);

/// <summary>
/// Known settings with their defaults (SPEC 2.3, "Einstellungen").
/// </summary>
public static class AppSettingKeys
{
    public static AppSettingKey<decimal> HoursPerDay { get; } = new(nameof(HoursPerDay), 8m);

    public static AppSettingKey<int> RetainerValuationMonths { get; } = new(nameof(RetainerValuationMonths), 12);

    public static AppSettingKey<string> DefaultCurrency { get; } = new(nameof(DefaultCurrency), "EUR");

    public static AppSettingKey<PricingModel> DefaultPricingModel { get; } = new(nameof(DefaultPricingModel), PricingModel.Hourly);
}
