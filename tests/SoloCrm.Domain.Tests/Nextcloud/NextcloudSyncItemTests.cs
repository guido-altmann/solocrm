using SoloCrm.Domain.Nextcloud;

namespace SoloCrm.Domain.Tests.Nextcloud;

public sealed class NextcloudSyncItemTests
{
    private static readonly string Hash = new('a', NextcloudSyncItem.ContentHashLength);

    [Fact]
    public void Constructor_NewItem_HasNoHashYet()
    {
        var id = Guid.CreateVersion7();

        var item = new NextcloudSyncItem(NextcloudItemType.Contact, id, "/dav/solocrm/x.vcf");

        item.EntityType.Should().Be(NextcloudItemType.Contact);
        item.EntityId.Should().Be(id);
        item.Href.Should().Be("/dav/solocrm/x.vcf");
        item.ContentHash.Should().BeEmpty();
        item.ETag.Should().BeNull();
    }

    [Fact]
    public void Constructor_UnknownType_Throws()
    {
        var act = () => new NextcloudSyncItem((NextcloudItemType)42, Guid.CreateVersion7(), "/x.vcf");

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Written_ETagAndHash_AreStored()
    {
        var item = new NextcloudSyncItem(NextcloudItemType.Contact, Guid.CreateVersion7(), "/x.vcf");

        item.Written("\"etag-1\"", Hash);

        item.ETag.Should().Be("\"etag-1\"");
        item.ContentHash.Should().Be(Hash);
    }

    [Fact]
    public void Written_NoETag_StoresNull()
    {
        var item = new NextcloudSyncItem(NextcloudItemType.Contact, Guid.CreateVersion7(), "/x.vcf");
        item.Written("\"etag-1\"", Hash);

        item.Written(" ", Hash);

        item.ETag.Should().BeNull();
    }

    [Fact]
    public void Written_MalformedHash_Throws()
    {
        var item = new NextcloudSyncItem(NextcloudItemType.Contact, Guid.CreateVersion7(), "/x.vcf");

        var act = () => item.Written("\"e\"", "abc");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ChangedRemotely_NewETag_ForgetsTheHashSoTheNextRunWritesAgain()
    {
        var item = new NextcloudSyncItem(NextcloudItemType.Task, Guid.CreateVersion7(), "/x.ics");
        item.Written("\"etag-1\"", Hash);

        item.ChangedRemotely("\"etag-2\"");

        item.ETag.Should().Be("\"etag-2\"");
        item.ContentHash.Should().BeEmpty();
    }
}
