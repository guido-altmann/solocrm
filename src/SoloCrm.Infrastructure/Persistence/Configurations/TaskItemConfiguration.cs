using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoloCrm.Domain.Tasks;

namespace SoloCrm.Infrastructure.Persistence.Configurations;

internal sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.ToTable("tasks");
        builder.Property(t => t.Title).HasMaxLength(TaskItem.TitleMaxLength);
        builder.Ignore(t => t.IsCompleted);

        // Deleting a contact (GDPR, US-20) deletes the tasks linked to it; organizations and
        // opportunities are only archived, a hard delete would turn their tasks into free tasks.
        builder.HasOne(t => t.Contact)
            .WithMany()
            .HasForeignKey(t => t.ContactId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Organization)
            .WithMany()
            .HasForeignKey(t => t.OrganizationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.Opportunity)
            .WithMany()
            .HasForeignKey(t => t.OpportunityId)
            .OnDelete(DeleteBehavior.SetNull);

        // "Heute": open tasks (completed_at IS NULL) by due date.
        builder.HasIndex(t => new { t.CompletedAt, t.DueDate });
    }
}
