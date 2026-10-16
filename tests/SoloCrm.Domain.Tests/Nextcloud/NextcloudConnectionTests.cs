using SoloCrm.Domain.Nextcloud;

namespace SoloCrm.Domain.Tests.Nextcloud;

public sealed class NextcloudConnectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.FromHours(2));

    private static NextcloudConnection Create() => new(" https://cloud.example.test/ ", " guido ", "protected-1", Now);

    [Fact]
    public void Constructor_ValidValues_NormalizesAndStoresUtcTime()
    {
        var connection = Create();

        connection.ServerUrl.Should().Be("https://cloud.example.test");
        connection.UserName.Should().Be("guido");
        connection.ProtectedAppPassword.Should().Be("protected-1");
        connection.ConnectedAt.Should().Be(Now);
        connection.ConnectedAt.Offset.Should().Be(TimeSpan.Zero);
        connection.AddressBookHref.Should().BeNull();
        connection.IsCredentialRejected.Should().BeFalse();
    }

    [Theory]
    [InlineData("", "guido", "p")]
    [InlineData("https://cloud.example.test", " ", "p")]
    [InlineData("https://cloud.example.test", "guido", "")]
    public void Constructor_BlankValue_Throws(string serverUrl, string userName, string password)
    {
        var act = () => new NextcloudConnection(serverUrl, userName, password, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("https://cloud.example.test/", "guido", true)]
    [InlineData("HTTPS://CLOUD.example.test", " guido ", true)]
    [InlineData("https://other.example.test", "guido", false)]
    [InlineData("https://cloud.example.test", "Guido", false)]
    public void IsSameAccount_ServerAndUser_ComparesNormalized(string serverUrl, string userName, bool expected)
    {
        Create().IsSameAccount(serverUrl, userName).Should().Be(expected);
    }

    [Fact]
    public void RejectCredentials_Always_PausesUntilReconnect()
    {
        var connection = Create();
        connection.UseAddressBook("/dav/guido/solocrm/");

        connection.RejectCredentials("Anmeldung abgelehnt.");

        connection.IsCredentialRejected.Should().BeTrue();
        connection.LastError.Should().Be("Anmeldung abgelehnt.");

        connection.Reconnect("https://cloud.example.test", "guido", "protected-2", Now.AddHours(1));

        connection.IsCredentialRejected.Should().BeFalse();
        connection.LastError.Should().BeNull();
        connection.ProtectedAppPassword.Should().Be("protected-2");
        connection.AddressBookHref.Should().Be("/dav/guido/solocrm/");
    }

    [Fact]
    public void Reconnect_OtherAccount_ForgetsTheCollections()
    {
        var connection = Create();
        connection.UseAddressBook("/dav/guido/solocrm/");
        connection.RecordSuccess(Now);

        connection.Reconnect("https://cloud.example.test", "anna", "protected-2", Now.AddHours(1));

        connection.UserName.Should().Be("anna");
        connection.AddressBookHref.Should().BeNull();
        connection.LastSyncedAt.Should().BeNull();
    }

    [Fact]
    public void RecordSuccess_AfterFailure_ClearsTheError()
    {
        var connection = Create();
        connection.RecordFailure("Nextcloud ist gerade nicht erreichbar.");

        connection.RecordSuccess(Now);

        connection.LastError.Should().BeNull();
        connection.LastSyncedAt.Should().Be(Now);
    }

    [Fact]
    public void RecordFailure_LongMessage_IsTruncated()
    {
        var connection = Create();

        connection.RecordFailure(new string('x', NextcloudConnection.ErrorMaxLength + 50));

        connection.LastError.Should().HaveLength(NextcloudConnection.ErrorMaxLength).And.EndWith("…");
    }
}
