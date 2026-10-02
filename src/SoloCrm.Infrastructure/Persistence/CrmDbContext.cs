using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Activities;
using SoloCrm.Domain.ApiKeys;
using SoloCrm.Domain.Auditing;
using SoloCrm.Domain.Common;
using SoloCrm.Domain.Contacts;
using SoloCrm.Domain.Opportunities;
using SoloCrm.Domain.Organizations;
using SoloCrm.Domain.Settings;
using SoloCrm.Domain.Tags;
using SoloCrm.Domain.Tasks;
using SoloCrm.Domain.Webhooks;
using SoloCrm.Infrastructure.Identity;
using SoloCrm.Infrastructure.Persistence.Outbox;

namespace SoloCrm.Infrastructure.Persistence;

public class CrmDbContext(DbContextOptions options)
    : IdentityDbContext<ApplicationUser>(options), ICrmDbContext
{
    public DbSet<Contact> Contacts => Set<Contact>();

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Opportunity> Opportunities => Set<Opportunity>();

    public DbSet<Stage> Stages => Set<Stage>();

    public DbSet<Activity> Activities => Set<Activity>();

    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<ContactTag> ContactTags => Set<ContactTag>();

    public DbSet<OrganizationTag> OrganizationTags => Set<OrganizationTag>();

    public DbSet<OpportunityTag> OpportunityTags => Set<OpportunityTag>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>();

    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();

    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    public IQueryable<Guid> AuditedRecordIds(string entityType, string foreignKey, Guid principalId) =>
        AuditHistory.LinkedRecordIds(this, entityType, foreignKey, principalId);

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasPostgresExtension("citext");
        builder.HasPostgresExtension("pg_trgm");
        builder.ApplyConfigurationsFromAssembly(typeof(CrmDbContext).Assembly);
        ConfigureEntityConventions(builder);
    }

    /// <summary>
    /// Applies the conventions shared by all <see cref="Entity"/> types (SPEC 2.2).
    /// </summary>
    private static void ConfigureEntityConventions(ModelBuilder builder)
    {
        var entityTypes = builder.Model.GetEntityTypes()
            .Where(t => typeof(Entity).IsAssignableFrom(t.ClrType) && t.BaseType is null)
            .Select(t => t.ClrType)
            .ToList();

        foreach (var clrType in entityTypes)
        {
            var entity = builder.Entity(clrType);
            entity.HasKey(nameof(Entity.Id));
            // Ids are UUIDv7 assigned in the entity constructor, never by the database.
            entity.Property(nameof(Entity.Id)).ValueGeneratedNever();
            entity.Ignore(nameof(Entity.DomainEvents));

            if (typeof(IHasExtraFields).IsAssignableFrom(clrType))
            {
                entity.Property<IReadOnlyDictionary<string, string>>(nameof(IHasExtraFields.ExtraFields))
                    .HasColumnType(JsonColumn.ColumnType)
                    .HasConversion(JsonColumn.DictionaryConverter, JsonColumn.DictionaryComparer)
                    // Fills existing rows when the column is added; the entities always send a value.
                    .HasDefaultValueSql("'{}'::jsonb");
            }
        }
    }
}
