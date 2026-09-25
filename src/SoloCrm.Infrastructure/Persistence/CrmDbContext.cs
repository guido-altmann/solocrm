using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Common;
using SoloCrm.Domain.Contacts;
using SoloCrm.Infrastructure.Identity;

namespace SoloCrm.Infrastructure.Persistence;

public class CrmDbContext(DbContextOptions options)
    : IdentityDbContext<ApplicationUser>(options), ICrmDbContext
{
    public DbSet<Contact> Contacts => Set<Contact>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasPostgresExtension("citext");
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
        }
    }
}
