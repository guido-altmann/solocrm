using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("organizations");
        builder.Property(o => o.Name).HasMaxLength(Organization.NameMaxLength);
        builder.Property(o => o.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.Website).HasMaxLength(Organization.WebsiteMaxLength);
        builder.Property(o => o.City).HasMaxLength(Organization.CityMaxLength);
        builder.Property(o => o.Notes);

        builder.HasIndex(o => o.Name);

        // Search (ADR-007): full-text over name, website (split into words) and city; trigram similarity over the name.
        builder.HasTrigramIndex(nameof(Organization.Name), "ix_organizations_name_trgm");
        builder.HasSearchVector(
            ("name", 'A'),
            (SearchConfiguration.SplitIntoWords("website"), 'B'),
            ("city", 'B'));
    }
}
