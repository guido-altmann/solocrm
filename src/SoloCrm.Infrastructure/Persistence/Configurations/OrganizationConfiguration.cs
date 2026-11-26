using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoloCrm.Domain.Organizations;
using SoloCrm.Infrastructure.Persistence.Configurations.Shared;

namespace SoloCrm.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("organizations");
        builder.Property(o => o.Name).HasMaxLength(Organization.NameMaxLength);
        builder.Property(o => o.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.Website).HasMaxLength(Organization.WebsiteMaxLength);
        builder.Property(o => o.Phone).HasMaxLength(Organization.PhoneMaxLength);
        // citext like contacts.email, so the mail sync matches case-insensitively (SOL-50); not unique (SOL-49).
        // citext has no type modifier, so the length is enforced by the validator and a check constraint.
        builder.Property(o => o.Email).HasColumnType("citext");
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_organizations_email_length",
            $"char_length(email) <= {Organization.EmailMaxLength}"));
        // The city keeps its column "city" (part of the search vector); the other parts are new nullable columns.
        builder.ComplexProperty(o => o.Address, address =>
        {
            address.ConfigureAddress();
            address.Property(a => a.City).HasColumnName("city");
        });
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
