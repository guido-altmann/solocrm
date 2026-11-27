using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoloCrm.Domain.Enrichment;

namespace SoloCrm.Infrastructure.Persistence.Configurations;

internal sealed class EnrichmentRequestConfiguration : IEntityTypeConfiguration<EnrichmentRequest>
{
    public void Configure(EntityTypeBuilder<EnrichmentRequest> builder)
    {
        builder.ToTable("enrichment_requests");

        builder.Property(r => r.TargetType).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Provider).HasMaxLength(EnrichmentRequest.ProviderMaxLength);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Error).HasMaxLength(EnrichmentRequest.ErrorMaxLength);

        // At most one active request per record (ADR-016).
        builder.HasIndex(r => new { r.TargetType, r.TargetId })
            .IsUnique()
            .HasFilter("status IN ('Pending', 'Running')");

        // The processor polls active requests.
        builder.HasIndex(r => r.RequestedAt).HasFilter("status IN ('Pending', 'Running')");

        // The daily cost cap counts requests since midnight.
        builder.HasIndex(r => r.CreatedAt);
    }
}
