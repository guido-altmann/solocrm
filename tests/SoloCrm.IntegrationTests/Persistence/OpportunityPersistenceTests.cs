using Microsoft.EntityFrameworkCore;
using SoloCrm.Domain.Auditing;
using SoloCrm.Domain.Opportunities;
using SoloCrm.Infrastructure.Persistence;
using SoloCrm.Infrastructure.Persistence.Seeding;
using SoloCrm.IntegrationTests.Features;

namespace SoloCrm.IntegrationTests.Persistence;

/// <summary>
/// Verifies nullable complex types (ADR-011), the stage seed and complex type auditing against Postgres.
/// </summary>
public sealed class OpportunityPersistenceTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    [Fact]
    public async Task Migrate_FreshDatabase_SeedsSixDefaultStages()
    {
        await using var db = OpenDb();

        var stages = await db.Stages.OrderBy(s => s.SortOrder).ToListAsync(Ct);

        stages.Select(s => (s.Id, s.Name, s.Status)).Should().Equal(
            (DefaultStages.New, "Neu", StageStatus.Open),
            (DefaultStages.Applied, "Beworben", StageStatus.Open),
            (DefaultStages.InTalks, "Im Gespräch", StageStatus.Open),
            (DefaultStages.Offer, "Angebot", StageStatus.Open),
            (DefaultStages.Won, "Gewonnen", StageStatus.Won),
            (DefaultStages.Lost, "Verloren", StageStatus.Lost));
    }

    [Fact]
    public async Task SaveAndLoad_WithPricingAndDuration_RoundTripsValues()
    {
        var pricing = Pricing.Create(PricingModel.Daily, 760.50m, "CHF");
        var duration = Duration.Create(6, DurationUnit.Months);
        var id = await AddAsync(new OpportunityDetails(Pricing: pricing, Duration: duration, Utilization: 80));

        await using var db = OpenDb();
        var loaded = await db.Opportunities.SingleAsync(o => o.Id == id, Ct);
        loaded.Pricing.Should().Be(pricing);
        loaded.Duration.Should().Be(duration);
        loaded.Utilization.Should().Be(80);
    }

    [Fact]
    public async Task SaveAndLoad_WithoutPricingAndDuration_LoadsNull()
    {
        var id = await AddAsync(new OpportunityDetails());

        await using var db = OpenDb();
        var loaded = await db.Opportunities.SingleAsync(o => o.Id == id, Ct);
        loaded.Pricing.Should().BeNull();
        loaded.Duration.Should().BeNull();
    }

    [Fact]
    public async Task Update_ValueToNullAndBack_PersistsEachStateAndAuditsFields()
    {
        var id = await AddAsync(new OpportunityDetails(Pricing: Pricing.Create(PricingModel.Hourly, 95m)));

        await UpdateAsync(id, new OpportunityDetails(Duration: Duration.Create(3, DurationUnit.Weeks)));
        await using (var db = OpenDb())
        {
            var loaded = await db.Opportunities.SingleAsync(o => o.Id == id, Ct);
            loaded.Pricing.Should().BeNull();
            loaded.Duration.Should().Be(Duration.Create(3, DurationUnit.Weeks));
        }

        await UpdateAsync(id, new OpportunityDetails(Pricing: Pricing.Create(PricingModel.Retainer, 2500m)));
        await using (var db = OpenDb())
        {
            var loaded = await db.Opportunities.SingleAsync(o => o.Id == id, Ct);
            loaded.Pricing.Should().Be(Pricing.Create(PricingModel.Retainer, 2500m));
            loaded.Duration.Should().BeNull();
        }

        await using var verify = OpenDb();
        var updates = await verify.AuditEntries
            .Where(a => a.EntityId == id && a.Action == AuditAction.Updated)
            .OrderBy(a => a.Id)
            .ToListAsync(Ct);
        updates.Should().HaveCount(2);
        updates[0].Changes.Should().BeEquivalentTo(
        [
            new AuditChange("Pricing.Model", "Hourly", null),
            new AuditChange("Pricing.Amount", "95", null),
            new AuditChange("Pricing.Currency", "EUR", null),
            new AuditChange("Duration.Value", null, "3"),
            new AuditChange("Duration.Unit", null, "Weeks"),
        ]);
        updates[1].Changes.Should().BeEquivalentTo(
        [
            new AuditChange("Pricing.Model", null, "Retainer"),
            new AuditChange("Pricing.Amount", null, "2500"),
            new AuditChange("Pricing.Currency", null, "EUR"),
            new AuditChange("Duration.Value", "3", null),
            new AuditChange("Duration.Unit", "Weeks", null),
        ]);
    }

    [Fact]
    public async Task Update_OnlyAmountChanged_AuditsOnlyAmount()
    {
        var id = await AddAsync(new OpportunityDetails(Pricing: Pricing.Create(PricingModel.Hourly, 95m)));

        await UpdateAsync(id, new OpportunityDetails(Pricing: Pricing.Create(PricingModel.Hourly, 105m)));

        await using var db = OpenDb();
        (await db.AuditEntries.SingleAsync(a => a.Action == AuditAction.Updated, Ct))
            .Changes.Should().Equal(new AuditChange("Pricing.Amount", "95", "105"));
    }

    [Fact]
    public async Task Create_WithPricing_AuditsComplexFieldsAsCreated()
    {
        var id = await AddAsync(new OpportunityDetails(Pricing: Pricing.Create(PricingModel.FixedPrice, 8000m)));

        await using var db = OpenDb();
        (await db.AuditEntries.SingleAsync(a => a.EntityId == id, Ct)).Changes.Should().Contain(
        [
            new AuditChange("Title", null, "Migration Azure"),
            new AuditChange("StageId", null, DefaultStages.New.ToString()),
            new AuditChange("Pricing.Model", null, "FixedPrice"),
            new AuditChange("Pricing.Amount", null, "8000"),
        ]);
    }

    [Fact]
    public async Task DeleteStage_WithOpportunities_IsRestricted()
    {
        await AddAsync(new OpportunityDetails());

        await using var db = CreateContext();
        db.Stages.Remove(await db.Stages.SingleAsync(s => s.Id == DefaultStages.New, Ct));
        var act = () => db.SaveChangesAsync(Ct);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    private async Task<Guid> AddAsync(OpportunityDetails details)
    {
        await using var db = CreateContext();
        var stage = await db.Stages.SingleAsync(s => s.Id == DefaultStages.New, Ct);
        var opportunity = Opportunity.Create("Migration Azure", stage, new DateOnly(2026, 9, 1), details);
        db.Opportunities.Add(opportunity);
        await db.SaveChangesAsync(Ct);
        return opportunity.Id;
    }

    private async Task UpdateAsync(Guid id, OpportunityDetails details)
    {
        await using var db = CreateContext();
        var opportunity = await db.Opportunities.SingleAsync(o => o.Id == id, Ct);
        opportunity.Update(opportunity.Title, opportunity.ReceivedOn, details);
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>The production context incl. interceptors.</summary>
    private CrmDbContext CreateContext() => Get<IDbContextFactory<CrmDbContext>>().CreateDbContext();
}
