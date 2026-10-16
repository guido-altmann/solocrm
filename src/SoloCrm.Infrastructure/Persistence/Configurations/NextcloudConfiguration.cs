using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoloCrm.Domain.Nextcloud;

namespace SoloCrm.Infrastructure.Persistence.Configurations;

internal sealed class NextcloudConnectionConfiguration : IEntityTypeConfiguration<NextcloudConnection>
{
    public void Configure(EntityTypeBuilder<NextcloudConnection> builder)
    {
        builder.ToTable("nextcloud_connections");

        builder.Property(c => c.ServerUrl).HasMaxLength(NextcloudConnection.ServerUrlMaxLength);
        builder.Property(c => c.UserName).HasMaxLength(NextcloudConnection.UserNameMaxLength);
        builder.Property(c => c.ProtectedAppPassword);
        builder.Property(c => c.AddressBookHref).HasMaxLength(NextcloudConnection.HrefMaxLength);
        builder.Property(c => c.TaskListHref).HasMaxLength(NextcloudConnection.HrefMaxLength);
        builder.Property(c => c.TaskSyncToken).HasMaxLength(NextcloudConnection.SyncTokenMaxLength);
        builder.Property(c => c.LastError).HasMaxLength(NextcloudConnection.ErrorMaxLength);
    }
}

internal sealed class NextcloudSyncItemConfiguration : IEntityTypeConfiguration<NextcloudSyncItem>
{
    public void Configure(EntityTypeBuilder<NextcloudSyncItem> builder)
    {
        builder.ToTable("nextcloud_sync_items");

        builder.Property(i => i.EntityType).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.Href).HasMaxLength(NextcloudConnection.HrefMaxLength);
        builder.Property(i => i.ETag).HasColumnName("etag").HasMaxLength(NextcloudSyncItem.ETagMaxLength);
        builder.Property(i => i.ContentHash).HasMaxLength(NextcloudSyncItem.ContentHashLength);

        // No foreign key to contacts or tasks (ADR-014): the row outlives a hard delete until the copy is removed.
        builder.HasIndex(i => new { i.EntityType, i.EntityId }).IsUnique();

        // The incoming phase maps changed resources back by their path.
        builder.HasIndex(i => i.Href);
    }
}
