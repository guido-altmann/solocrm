using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoloCrm.Domain.ApiKeys;

namespace SoloCrm.Infrastructure.Persistence.Configurations;

internal sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("api_keys");
        builder.Property(k => k.Name).HasMaxLength(ApiKey.NameMaxLength);
        builder.Property(k => k.Prefix).HasMaxLength(ApiKey.PrefixLength);
        builder.Property(k => k.KeyHash).HasMaxLength(64);
        builder.Ignore(k => k.IsRevoked);

        // Every API request looks the key up by its prefix.
        builder.HasIndex(k => k.Prefix).IsUnique();
    }
}
