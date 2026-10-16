using SoloCrm.Domain.Common;

namespace SoloCrm.Domain.Nextcloud;

/// <summary>Kind of record that is mirrored to Nextcloud.</summary>
public enum NextcloudItemType
{
    Contact,
    Task,
}

/// <summary>
/// Links a record to its copy in Nextcloud (ADR-014): where it lives, its ETag and the hash of the payload sent last.
/// There is deliberately no foreign key to the record: after a hard delete the row stays until the next sync has
/// deleted the copy in Nextcloud.
/// </summary>
public sealed class NextcloudSyncItem : Entity
{
    public const int ETagMaxLength = 200;
    public const int ContentHashLength = 64;

    // Required by EF Core.
    private NextcloudSyncItem()
    {
        Href = null!;
        ContentHash = null!;
    }

    public NextcloudSyncItem(NextcloudItemType entityType, Guid entityId, string href)
    {
        EntityType = Enum.IsDefined(entityType)
            ? entityType
            : throw new ArgumentOutOfRangeException(nameof(entityType), entityType, "Unknown item type.");
        EntityId = entityId;
        Href = RequiredHref(href);
        ContentHash = "";
    }

    public NextcloudItemType EntityType { get; private set; }

    /// <summary>Id of the contact or task; not a foreign key.</summary>
    public Guid EntityId { get; private set; }

    /// <summary>Path of the resource on the server, e.g. <c>/remote.php/dav/addressbooks/users/me/solocrm/&lt;id&gt;.vcf</c>.</summary>
    public string Href { get; private set; }

    /// <summary>ETag after the last write; <c>null</c> if the server did not report one.</summary>
    public string? ETag { get; private set; }

    /// <summary>SHA-256 (hex) of the payload sent last; empty before the first successful write.</summary>
    public string ContentHash { get; private set; }

    /// <summary>Notes a successful write of the payload with hash <paramref name="contentHash"/>.</summary>
    public void Written(string? etag, string contentHash)
    {
        ETag = string.IsNullOrWhiteSpace(etag) || etag.Length > ETagMaxLength ? null : etag;
        ContentHash = contentHash.Length == ContentHashLength
            ? contentHash
            : throw new ArgumentException($"A hash of {ContentHashLength} hex characters is required.", nameof(contentHash));
    }

    private static string RequiredHref(string href) =>
        string.IsNullOrWhiteSpace(href) || href.Length > NextcloudConnection.HrefMaxLength
            ? throw new ArgumentException($"A href of 1 to {NextcloudConnection.HrefMaxLength} characters is required.", nameof(href))
            : href;
}
