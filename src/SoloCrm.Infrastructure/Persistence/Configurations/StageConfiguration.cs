using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoloCrm.Domain.Opportunities;
using SoloCrm.Infrastructure.Persistence.Seeding;

namespace SoloCrm.Infrastructure.Persistence.Configurations;

internal sealed class StageConfiguration : IEntityTypeConfiguration<Stage>
{
    public void Configure(EntityTypeBuilder<Stage> builder)
    {
        builder.ToTable("stages");
        builder.Property(s => s.Name).HasMaxLength(Stage.NameMaxLength);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(10);
        builder.HasData(DefaultStages.SeedData);
    }
}
