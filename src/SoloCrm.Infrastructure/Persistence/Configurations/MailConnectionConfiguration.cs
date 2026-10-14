using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoloCrm.Domain.Mail;

namespace SoloCrm.Infrastructure.Persistence.Configurations;

internal sealed class MailConnectionConfiguration : IEntityTypeConfiguration<MailConnection>
{
    public void Configure(EntityTypeBuilder<MailConnection> builder)
    {
        builder.ToTable("mail_connections");

        builder.Property(c => c.Provider).HasMaxLength(MailMessage.ProviderMaxLength);
        builder.Property(c => c.Account).HasMaxLength(MailConnection.AccountMaxLength);
        builder.Property(c => c.ProtectedRefreshToken);
        builder.Property(c => c.LastError).HasMaxLength(MailConnection.ErrorMaxLength);

        builder.HasIndex(c => c.Provider).IsUnique();
    }
}
