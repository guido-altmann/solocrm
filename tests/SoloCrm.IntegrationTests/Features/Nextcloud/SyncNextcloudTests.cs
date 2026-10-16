using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Contacts;
using SoloCrm.Application.Features.Nextcloud;
using SoloCrm.Domain.Contacts;
using SoloCrm.Domain.Nextcloud;
using SoloCrm.Domain.Organizations;
using SoloCrm.Infrastructure.Nextcloud;

namespace SoloCrm.IntegrationTests.Features.Nextcloud;

/// <summary>Connecting and the contact sync end to end, against Postgres and a real Radicale server (ADR-014).</summary>
public sealed class SyncNextcloudTests(PostgresFixture postgres, RadicaleFixture radicale)
    : HandlerTest(postgres), IClassFixture<RadicaleFixture>
{
    private readonly SocketsHttpHandler _handler = new() { AllowAutoRedirect = false };

    // Radicale accepts every user name; a fresh one isolates the test.
    private readonly string _user = $"user-{Guid.NewGuid():N}";

    private NextcloudDavClient Target => new(new SharedHandlerFactory(_handler), NullLogger<NextcloudDavClient>.Instance);

    private string AddressBook => $"/{_user}/solocrm/";

    public override async ValueTask DisposeAsync()
    {
        _handler.Dispose();
        await base.DisposeAsync();
    }

    private ConnectNextcloud.Handler ConnectHandler() => new(
        Get<ICrmDbContextFactory>(), Target, Get<ISecretProtector>(), new ConnectNextcloud.Validator(new NextcloudServers([radicale.Host])), Time);

    private SyncNextcloud.Handler SyncHandler() => new(Get<ICrmDbContextFactory>(), Target, Get<ISecretProtector>(), Time);

    private async Task ConnectAsync()
    {
        var result = await ConnectHandler().Handle(new ConnectNextcloud.Command(radicale.BaseUrl.AbsoluteUri, _user, "app-password"), Ct);
        result.IsSuccess.Should().BeTrue();
    }

    private async Task<SyncNextcloud.Result> SyncAsync()
    {
        var result = await SyncHandler().Handle(new SyncNextcloud.Command(), Ct);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    private async Task<Contact> AddContactAsync(string firstName, Organization? organization = null, bool archived = false)
    {
        await using var db = OpenDb();
        if (organization is not null)
        {
            db.Organizations.Add(organization);
        }

        var contact = Contact.Create(firstName, "Mustermann", $"{firstName.ToLowerInvariant()}@example.test", organizationId: organization?.Id);
        if (archived)
        {
            contact.Archive();
        }

        db.Contacts.Add(contact);
        await db.SaveChangesAsync(Ct);
        return contact;
    }

    private async Task<HttpResponseMessage> RemoteAsync(HttpMethod method, string href, string? body = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(radicale.BaseUrl, href));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_user}:x")));
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "text/vcard");
        }

        using var client = new HttpClient(_handler, disposeHandler: false);
        return await client.SendAsync(request, Ct);
    }

    private async Task<string?> RemoteCardAsync(Guid contactId)
    {
        using var response = await RemoteAsync(HttpMethod.Get, AddressBook + $"{contactId}.vcf");
        return response.StatusCode == HttpStatusCode.NotFound ? null : await response.Content.ReadAsStringAsync(Ct);
    }

    [Fact]
    public async Task Connect_ValidCredentials_CreatesAddressBookAndStoresEncryptedPassword()
    {
        var result = await ConnectHandler().Handle(new ConnectNextcloud.Command(radicale.BaseUrl.AbsoluteUri, $" {_user} ", "app-password"), Ct);

        result.Value.UserName.Should().Be(_user);
        await using var db = OpenDb();
        var connection = await db.NextcloudConnections.SingleAsync(Ct);
        connection.AddressBookHref.Should().Be(AddressBook);
        connection.ServerUrl.Should().Be(radicale.BaseUrl.AbsoluteUri.TrimEnd('/'));
        connection.ProtectedAppPassword.Should().NotContain("app-password");
        Get<ISecretProtector>().Unprotect(connection.ProtectedAppPassword).Should().Be("app-password");
    }

    [Fact]
    public async Task Connect_HttpServerNotAllowed_ReturnsValidationErrorAndStoresNothing()
    {
        var handler = new ConnectNextcloud.Handler(
            Get<ICrmDbContextFactory>(), Target, Get<ISecretProtector>(), new ConnectNextcloud.Validator(NextcloudServers.HttpsOnly), Time);

        var result = await handler.Handle(new ConnectNextcloud.Command(radicale.BaseUrl.AbsoluteUri, _user, "app-password"), Ct);

        result.Error.Should().BeOfType<ValidationError>()
            .Which.Errors.Should().ContainKey(nameof(ConnectNextcloud.Command.ServerUrl));
        await using var db = OpenDb();
        (await db.NextcloudConnections.AnyAsync(Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Sync_NewContacts_WritesVCardsIncludingOrganization()
    {
        await ConnectAsync();
        var max = await AddContactAsync("Max", Organization.Create("ACME GmbH"));
        var erika = await AddContactAsync("Erika");

        var result = await SyncAsync();

        result.Should().Be(new SyncNextcloud.Result(Written: 2, Deleted: 0, Unchanged: 0));
        (await RemoteCardAsync(max.Id)).Should().Contain("FN:Max Mustermann").And.Contain("ORG:ACME GmbH").And.Contain($"UID:{max.Id}@solocrm");
        (await RemoteCardAsync(erika.Id)).Should().Contain("EMAIL;TYPE=INTERNET:erika@example.test");
        await using var db = OpenDb();
        var items = await db.NextcloudSyncItems.ToListAsync(Ct);
        items.Should().HaveCount(2).And.OnlyContain(i => i.EntityType == NextcloudItemType.Contact && i.ETag != null && i.ContentHash.Length == 64);
        (await db.NextcloudConnections.SingleAsync(Ct)).LastSyncedAt.Should().Be(Start);
    }

    [Fact]
    public async Task Sync_NothingChanged_WritesNothing()
    {
        await ConnectAsync();
        await AddContactAsync("Max");
        await SyncAsync();

        var result = await SyncAsync();

        result.Should().Be(new SyncNextcloud.Result(Written: 0, Deleted: 0, Unchanged: 1));
    }

    [Fact]
    public async Task Sync_OrganizationRenamed_UpdatesOrgOfItsContacts()
    {
        await ConnectAsync();
        var organization = Organization.Create("ACME GmbH");
        var max = await AddContactAsync("Max", organization);
        await SyncAsync();

        await using (var db = OpenDb())
        {
            var stored = await db.Organizations.SingleAsync(o => o.Id == organization.Id, Ct);
            stored.Update("ACME Holding AG", stored.Type, null, null, null);
            await db.SaveChangesAsync(Ct);
        }

        var result = await SyncAsync();

        result.Written.Should().Be(1);
        (await RemoteCardAsync(max.Id)).Should().Contain("ORG:ACME Holding AG");
    }

    [Fact]
    public async Task Sync_ContactArchived_DeletesTheCopy()
    {
        await ConnectAsync();
        var max = await AddContactAsync("Max");
        await SyncAsync();
        (await SendAsync<ArchiveContact.Command, ArchiveContact.Result>(new ArchiveContact.Command(max.Id))).IsSuccess.Should().BeTrue();

        var result = await SyncAsync();

        result.Deleted.Should().Be(1);
        (await RemoteCardAsync(max.Id)).Should().BeNull();
        await using var db = OpenDb();
        (await db.NextcloudSyncItems.AnyAsync(Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Sync_ArchivedContact_IsNeverWritten()
    {
        await ConnectAsync();
        var archived = await AddContactAsync("Max", archived: true);

        var result = await SyncAsync();

        result.Should().Be(new SyncNextcloud.Result(0, 0, 0));
        (await RemoteCardAsync(archived.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Sync_ContactErased_DeletesTheCopy()
    {
        await ConnectAsync();
        var max = await AddContactAsync("Max");
        await SyncAsync();
        var erased = await SendAsync<DeleteContactPermanently.Command, DeleteContactPermanently.Result>(
            new DeleteContactPermanently.Command(max.Id, "Max Mustermann"));
        erased.IsSuccess.Should().BeTrue();

        var result = await SyncAsync();

        result.Deleted.Should().Be(1);
        (await RemoteCardAsync(max.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Sync_CopyChangedInNextcloud_IsOverwrittenOnTheNextChange()
    {
        await ConnectAsync();
        var max = await AddContactAsync("Max");
        await SyncAsync();
        var remote = await RemoteCardAsync(max.Id);
        using (await RemoteAsync(HttpMethod.Put, AddressBook + $"{max.Id}.vcf", remote!.Replace("Max Mustermann", "Maximilian Mustermann", StringComparison.Ordinal)))
        {
        }

        await using (var db = OpenDb())
        {
            var contact = await db.Contacts.SingleAsync(Ct);
            contact.Update(contact.FirstName, contact.LastName, contact.Email, "+49 30 1234", null, null, null, null);
            await db.SaveChangesAsync(Ct);
        }

        var result = await SyncAsync();

        result.Written.Should().Be(1);
        (await RemoteCardAsync(max.Id)).Should().Contain("FN:Max Mustermann").And.Contain("+49 30 1234");
    }

    [Fact]
    public async Task Sync_ForeignEntryInAddressBook_IsLeftAlone()
    {
        await ConnectAsync();
        using (await RemoteAsync(HttpMethod.Put, AddressBook + "foreign.vcf", "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:foreign\r\nFN:Anna\r\nN:;Anna;;;\r\nEND:VCARD\r\n"))
        {
        }

        await AddContactAsync("Max");
        await SyncAsync();

        using var response = await RemoteAsync(HttpMethod.Get, AddressBook + "foreign.vcf");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Sync_NotConnected_ReturnsError()
    {
        var result = await SyncHandler().Handle(new SyncNextcloud.Command(), Ct);

        result.Error.Should().Be(NextcloudErrors.NotConnected);
    }

    [Fact]
    public async Task Disconnect_Connected_RemovesConnectionAndMappingButKeepsTheAddressBook()
    {
        await ConnectAsync();
        var max = await AddContactAsync("Max");
        await SyncAsync();

        var result = await SendAsync<DisconnectNextcloud.Command, DisconnectNextcloud.Result>(new DisconnectNextcloud.Command());

        result.IsSuccess.Should().BeTrue();
        await using var db = OpenDb();
        (await db.NextcloudConnections.AnyAsync(Ct)).Should().BeFalse();
        (await db.NextcloudSyncItems.AnyAsync(Ct)).Should().BeFalse();
        (await RemoteCardAsync(max.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task GetStatus_AfterSync_ReportsAccountAndContactCount()
    {
        await ConnectAsync();
        await AddContactAsync("Max");
        await SyncAsync();

        var status = await QueryAsync<GetNextcloudStatus.Query, GetNextcloudStatus.Result>(new GetNextcloudStatus.Query());

        status.Value.Connection.Should().BeEquivalentTo(new
        {
            UserName = _user,
            LastSyncedAt = (DateTimeOffset?)Start,
            LastError = (string?)null,
            IsCredentialRejected = false,
            Contacts = 1,
        });
    }

    private sealed class SharedHandlerFactory(SocketsHttpHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
