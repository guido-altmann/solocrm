using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoloCrm.Domain.Settings;

namespace SoloCrm.Infrastructure.Persistence.Configurations;

internal sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ToTable("app_settings");
        builder.Property(s => s.Key).HasMaxLength(AppSetting.KeyMaxLength);
        builder.HasIndex(s => s.Key).IsUnique();
        builder.Property(s => s.Value).HasColumnType(JsonColumn.ColumnType);
    }
}
