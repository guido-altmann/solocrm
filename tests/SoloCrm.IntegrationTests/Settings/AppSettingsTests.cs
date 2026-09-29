using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Opportunities;
using SoloCrm.Domain.Settings;
using SoloCrm.IntegrationTests.Web;

namespace SoloCrm.IntegrationTests.Settings;

[Trait("Category", "Integration")]
public sealed class AppSettingsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private string _connectionString = "";
    private ServiceProvider _services = null!;

    private IAppSettings Settings => _services.GetRequiredService<IAppSettings>();

    public async ValueTask InitializeAsync()
    {
        _connectionString = await CrmWebApplicationFactory.CreateDatabaseAsync(postgres, TestContext.Current.CancellationToken);
        _services = CrmServices.Create(_connectionString, new FakeTimeProvider());
    }

    public async ValueTask DisposeAsync() => await _services.DisposeAsync();

    [Fact]
    public async Task GetAsync_NothingStored_ReturnsDefaults()
    {
        var ct = TestContext.Current.CancellationToken;

        (await Settings.GetAsync(AppSettingKeys.HoursPerDay, ct)).Should().Be(8m);
        (await Settings.GetAsync(AppSettingKeys.RetainerValuationMonths, ct)).Should().Be(12);
        (await Settings.GetAsync(AppSettingKeys.DefaultCurrency, ct)).Should().Be("EUR");
        (await Settings.GetAsync(AppSettingKeys.DefaultPricingModel, ct)).Should().Be(PricingModel.Hourly);
    }

    [Fact]
    public async Task SetAsync_ThenGetAsync_ReturnsStoredValues()
    {
        var ct = TestContext.Current.CancellationToken;

        await Settings.SetAsync(AppSettingKeys.HoursPerDay, 7.5m, ct);
        await Settings.SetAsync(AppSettingKeys.DefaultPricingModel, PricingModel.Daily, ct);
        await Settings.SetAsync(AppSettingKeys.HoursPerDay, 7m, ct);

        (await Settings.GetAsync(AppSettingKeys.HoursPerDay, ct)).Should().Be(7m);
        (await Settings.GetAsync(AppSettingKeys.DefaultPricingModel, ct)).Should().Be(PricingModel.Daily);
    }

    [Fact]
    public async Task GetAsync_UnreadableValue_ReturnsDefault()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var db = CrmWebApplicationFactory.CreateDbContext(_connectionString))
        {
            db.AppSettings.Add(new AppSetting(AppSettingKeys.DefaultPricingModel.Name, "\"Barter\""));
            await db.SaveChangesAsync(ct);
        }

        (await Settings.GetAsync(AppSettingKeys.DefaultPricingModel, ct)).Should().Be(PricingModel.Hourly);
    }
}
