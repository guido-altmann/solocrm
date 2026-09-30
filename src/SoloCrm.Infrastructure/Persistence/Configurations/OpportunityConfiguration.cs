using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoloCrm.Domain.Opportunities;

namespace SoloCrm.Infrastructure.Persistence.Configurations;

internal sealed class OpportunityConfiguration : IEntityTypeConfiguration<Opportunity>
{
    public void Configure(EntityTypeBuilder<Opportunity> builder)
    {
        builder.ToTable("opportunities", t =>
        {
            t.HasCheckConstraint("ck_opportunities_utilization", "utilization BETWEEN 0 AND 100");
            t.HasCheckConstraint("ck_opportunities_remote_percentage", "remote_percentage BETWEEN 0 AND 100");
            t.HasCheckConstraint("ck_opportunities_pricing_amount", "pricing_amount > 0");
            t.HasCheckConstraint("ck_opportunities_duration_value", "duration_value > 0");
        });

        builder.Property(o => o.Title).HasMaxLength(Opportunity.TitleMaxLength);
        builder.Property(o => o.Source).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.LostReason).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(o => o.Status);

        // Nullable complex types (EF Core 10): columns live in "opportunities" and are all NULL when the value is null.
        builder.ComplexProperty(o => o.Pricing, pricing =>
        {
            pricing.Property(p => p.Model).HasConversion<string>().HasMaxLength(20);
            pricing.Property(p => p.Amount).HasPrecision(12, 2);
            pricing.Property(p => p.Currency).HasMaxLength(3);
        });

        builder.ComplexProperty(o => o.Duration, duration =>
        {
            duration.Property(d => d.Unit).HasConversion<string>().HasMaxLength(10);
        });

        // Stages with opportunities can only be deleted after reassigning them (DeleteStage, US-09 AK2).
        builder.HasOne(o => o.Stage)
            .WithMany()
            .HasForeignKey(o => o.StageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.ClientOrganization)
            .WithMany()
            .HasForeignKey(o => o.ClientOrganizationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(o => o.AgencyOrganization)
            .WithMany()
            .HasForeignKey(o => o.AgencyOrganizationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(o => o.PrimaryContact)
            .WithMany()
            .HasForeignKey(o => o.PrimaryContactId)
            .OnDelete(DeleteBehavior.SetNull);

        // Search (ADR-007): full-text and trigram similarity over the title.
        builder.HasTrigramIndex(nameof(Opportunity.Title), "ix_opportunities_title_trgm");
        builder.HasSearchVector(("title", 'A'));
    }
}
