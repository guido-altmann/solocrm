using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoloCrm.Domain.Mail;

namespace SoloCrm.Infrastructure.Persistence.Configurations;

internal sealed class MailSyncStateConfiguration : IEntityTypeConfiguration<MailSyncState>
{
    public void Configure(EntityTypeBuilder<MailSyncState> builder)
    {
        builder.ToTable("mail_sync_states");

        builder.Property(s => s.Provider).HasMaxLength(MailMessage.ProviderMaxLength);
        builder.Property(s => s.Folder).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.DeltaToken).HasMaxLength(MailSyncState.DeltaTokenMaxLength);

        builder.HasIndex(s => new { s.Provider, s.Folder }).IsUnique();
    }
}
