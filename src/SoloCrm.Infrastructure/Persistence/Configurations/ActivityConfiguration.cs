using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoloCrm.Domain.Activities;

namespace SoloCrm.Infrastructure.Persistence.Configurations;

internal sealed class ActivityConfiguration : IEntityTypeConfiguration<Activity>
{
    public void Configure(EntityTypeBuilder<Activity> builder)
    {
        builder.ToTable("activities", t => t.HasCheckConstraint(
            "ck_activities_linked_record_required",
            "contact_id IS NOT NULL OR organization_id IS NOT NULL OR opportunity_id IS NOT NULL"));

        builder.Property(a => a.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Subject).HasMaxLength(Activity.SubjectMaxLength);
        builder.Property(a => a.Body);

        // Activities belong to their records: deleting a contact (GDPR, US-20) deletes its activities.
        // Cascading instead of SET NULL also keeps the check constraint satisfiable.
        builder.HasOne(a => a.Contact)
            .WithMany()
            .HasForeignKey(a => a.ContactId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Organization)
            .WithMany()
            .HasForeignKey(a => a.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Opportunity)
            .WithMany()
            .HasForeignKey(a => a.OpportunityId)
            .OnDelete(DeleteBehavior.Cascade);

        // Timeline: the entries of one record, newest first. The indexes also cover the foreign keys.
        builder.HasIndex(a => new { a.ContactId, a.OccurredAt });
        builder.HasIndex(a => new { a.OrganizationId, a.OccurredAt });
        builder.HasIndex(a => new { a.OpportunityId, a.OccurredAt });
    }
}
