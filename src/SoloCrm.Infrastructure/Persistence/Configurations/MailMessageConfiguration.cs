using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoloCrm.Domain.Mail;

namespace SoloCrm.Infrastructure.Persistence.Configurations;

internal sealed class MailMessageConfiguration : IEntityTypeConfiguration<MailMessage>
{
    public void Configure(EntityTypeBuilder<MailMessage> builder)
    {
        builder.ToTable("mail_messages");

        builder.Property(m => m.Provider).HasMaxLength(MailMessage.ProviderMaxLength);
        builder.Property(m => m.ExternalId).HasMaxLength(MailMessage.ExternalIdMaxLength);
        builder.Property(m => m.InternetMessageId).HasMaxLength(MailMessage.InternetMessageIdMaxLength);
        builder.Property(m => m.Folder).HasConversion<string>().HasMaxLength(20);

        // Deleting the activity (e.g. GDPR erasure of its contact, US-20) deletes the link.
        builder.HasOne(m => m.Activity)
            .WithMany()
            .HasForeignKey(m => m.ActivityId)
            .OnDelete(DeleteBehavior.Cascade);

        // One activity per email, even if it shows up in Inbox and Sent or is delivered again.
        builder.HasIndex(m => new { m.Provider, m.InternetMessageId }).IsUnique();
        builder.HasIndex(m => m.ActivityId);
    }
}
