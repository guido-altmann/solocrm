using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoloCrm.Domain.Enrichment;

namespace SoloCrm.Infrastructure.Persistence.Configurations;

internal sealed class EnrichmentSuggestionConfiguration : IEntityTypeConfiguration<EnrichmentSuggestion>
{
    public void Configure(EntityTypeBuilder<EnrichmentSuggestion> builder)
    {
        builder.ToTable("enrichment_suggestions", t => t.HasCheckConstraint(
            "ck_enrichment_suggestions_confidence", "confidence BETWEEN 0 AND 1"));

        builder.Property(s => s.Field).HasConversion<string>().HasMaxLength(30);
        builder.Property(s => s.ProposedValue).HasMaxLength(EnrichmentSuggestion.ValueMaxLength);
        builder.Property(s => s.CurrentValue).HasMaxLength(EnrichmentSuggestion.ValueMaxLength);
        builder.Property(s => s.Confidence).HasPrecision(3, 2);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);

        // The URLs live in a text[] column; the entity exposes them read-only.
        builder.Ignore(s => s.SourceUrls);
        builder.Property<string[]>("_sourceUrls").HasColumnName("source_urls");

        builder.HasOne<EnrichmentRequest>().WithMany().HasForeignKey(s => s.RequestId).OnDelete(DeleteBehavior.Cascade);

        // The suggestions of a request are listed; also the foreign key index.
        builder.HasIndex(s => s.RequestId);
    }
}
