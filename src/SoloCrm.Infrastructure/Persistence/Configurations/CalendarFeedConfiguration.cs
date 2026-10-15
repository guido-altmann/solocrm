using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoloCrm.Domain.CalendarFeeds;

namespace SoloCrm.Infrastructure.Persistence.Configurations;

internal sealed class CalendarFeedConfiguration : IEntityTypeConfiguration<CalendarFeed>
{
    public void Configure(EntityTypeBuilder<CalendarFeed> builder)
    {
        builder.ToTable("calendar_feeds");
        builder.Property(f => f.TokenHash).HasMaxLength(64);

        // The feed is looked up by the hash of the token in the URL.
        builder.HasIndex(f => f.TokenHash).IsUnique();
    }
}
