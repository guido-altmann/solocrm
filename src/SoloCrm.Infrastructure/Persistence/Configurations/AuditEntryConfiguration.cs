using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoloCrm.Domain.Auditing;

namespace SoloCrm.Infrastructure.Persistence.Configurations;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_entries");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.EntityType).HasMaxLength(AuditEntry.EntityTypeMaxLength);
        builder.Property(a => a.Action).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Changes)
            .HasColumnType(JsonColumn.ColumnType)
            .HasConversion(JsonColumn.ListConverter<AuditChange>(), JsonColumn.ListComparer<AuditChange>());

        // Timeline (iteration 3): all entries of one object, newest first.
        builder.HasIndex(a => new { a.EntityId, a.OccurredAt });
    }
}
