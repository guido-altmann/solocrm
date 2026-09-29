using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Settings;
using SoloCrm.Infrastructure.Persistence;

namespace SoloCrm.Infrastructure.Settings;

internal sealed partial class AppSettings(IDbContextFactory<CrmDbContext> dbFactory, ILogger<AppSettings> logger) : IAppSettings
{
    public async Task<T> GetAsync<T>(AppSettingKey<T> key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var json = await db.AppSettings
            .Where(s => s.Key == key.Name)
            .Select(s => s.Value)
            .SingleOrDefaultAsync(cancellationToken);

        if (json is null)
        {
            return key.DefaultValue;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonColumn.Options) ?? key.DefaultValue;
        }
        catch (JsonException ex)
        {
            LogUnreadableSetting(ex, key.Name);
            return key.DefaultValue;
        }
    }

    public async Task SetAsync<T>(AppSettingKey<T> key, T value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);

        var json = JsonSerializer.Serialize(value, JsonColumn.Options);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var setting = await db.AppSettings.SingleOrDefaultAsync(s => s.Key == key.Name, cancellationToken);
        if (setting is null)
        {
            db.AppSettings.Add(new AppSetting(key.Name, json));
        }
        else
        {
            setting.SetValue(json);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Setting {Key} is unreadable; using the default value")]
    private partial void LogUnreadableSetting(Exception exception, string key);
}
