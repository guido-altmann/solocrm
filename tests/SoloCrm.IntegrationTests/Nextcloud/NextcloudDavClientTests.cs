using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SoloCrm.Application.Abstractions;
using SoloCrm.Infrastructure.Nextcloud;
using SoloCrm.IntegrationTests.Mail;

namespace SoloCrm.IntegrationTests.Nextcloud;

/// <summary>The DAV protocol against a real Radicale server (ADR-014).</summary>
[Trait("Category", "Integration")]
public sealed class NextcloudDavClientTests(RadicaleFixture radicale) : IClassFixture<RadicaleFixture>, IDisposable
{
    private static readonly DavContent Card = new("BEGIN:VCARD\r\nVERSION:3.0\r\nUID:1@solocrm\r\nFN:Max Mustermann\r\nN:Mustermann;Max;;;\r\nEND:VCARD\r\n", "text/vcard; charset=utf-8");

    private readonly RealHttp _http = new();

    // Radicale accepts every user name; a fresh one isolates the test.
    private readonly DavAccount _account = new(radicale.BaseUrl, $"user-{Guid.NewGuid():N}", "app-password");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private NextcloudDavClient Client() => new(_http, NullLogger<NextcloudDavClient>.Instance);

    public void Dispose() => _http.Dispose();

    [Fact]
    public async Task EnsureAddressBook_NewAccount_CreatesAddressBookSoloCrm()
    {
        var href = await Client().EnsureAddressBookAsync(_account, Ct);

        href.Should().Be($"/{_account.UserName}/solocrm/");
        var listing = await _http.SendAsync(_account, "PROPFIND", href, "<d:propfind xmlns:d=\"DAV:\"><d:prop><d:displayname/><d:resourcetype/></d:prop></d:propfind>", depth: "0");
        listing.Should().Contain("<displayname>SoloCRM</displayname>").And.Contain("addressbook");
    }

    [Fact]
    public async Task EnsureAddressBook_SecondTime_ReusesTheAddressBook()
    {
        var first = await Client().EnsureAddressBookAsync(_account, Ct);

        var second = await Client().EnsureAddressBookAsync(_account, Ct);

        second.Should().Be(first);
    }

    [Fact]
    public async Task EnsureAddressBook_PathTakenByOtherCollection_UsesTheNextPath()
    {
        await Client().EnsureAddressBookAsync(_account, Ct);
        await _http.SendAsync(_account, "PROPPATCH", $"/{_account.UserName}/solocrm/",
            "<d:propertyupdate xmlns:d=\"DAV:\"><d:set><d:prop><d:displayname>Privat</d:displayname></d:prop></d:set></d:propertyupdate>");

        var href = await Client().EnsureAddressBookAsync(_account, Ct);

        href.Should().Be($"/{_account.UserName}/solocrm-2/");
    }

    [Fact]
    public async Task Put_NewResource_CreatesItAndReturnsETag()
    {
        var href = await Client().EnsureAddressBookAsync(_account, Ct) + "1.vcf";

        var result = await Client().PutAsync(_account, href, Card, ifMatch: null, unconditional: false, Ct);

        result.PreconditionFailed.Should().BeFalse();
        result.ETag.Should().NotBeNullOrEmpty();
        (await _http.SendAsync(_account, "GET", href)).Should().Contain("FN:Max Mustermann");
    }

    [Fact]
    public async Task Put_ExistingResourceAsNew_ReportsPreconditionFailed()
    {
        var href = await Client().EnsureAddressBookAsync(_account, Ct) + "1.vcf";
        await Client().PutAsync(_account, href, Card, ifMatch: null, unconditional: false, Ct);

        var result = await Client().PutAsync(_account, href, Card, ifMatch: null, unconditional: false, Ct);

        result.Should().Be(DavWriteResult.Conflict);
    }

    [Fact]
    public async Task Put_WithCurrentETag_UpdatesAndOutdatedETagFails()
    {
        var href = await Client().EnsureAddressBookAsync(_account, Ct) + "1.vcf";
        var created = await Client().PutAsync(_account, href, Card, ifMatch: null, unconditional: false, Ct);
        var changed = Card with { Body = Card.Body.Replace("Max Mustermann", "Maxi Mustermann", StringComparison.Ordinal) };

        var updated = await Client().PutAsync(_account, href, changed, created.ETag, unconditional: false, Ct);
        var outdated = await Client().PutAsync(_account, href, Card, created.ETag, unconditional: false, Ct);

        updated.PreconditionFailed.Should().BeFalse();
        updated.ETag.Should().NotBe(created.ETag);
        outdated.PreconditionFailed.Should().BeTrue();
    }

    [Fact]
    public async Task Put_Unconditional_OverwritesAnyVersion()
    {
        var href = await Client().EnsureAddressBookAsync(_account, Ct) + "1.vcf";
        await Client().PutAsync(_account, href, Card, ifMatch: null, unconditional: false, Ct);
        var changed = Card with { Body = Card.Body.Replace("Max Mustermann", "Maxi Mustermann", StringComparison.Ordinal) };

        var result = await Client().PutAsync(_account, href, changed, "\"outdated\"", unconditional: true, Ct);

        result.PreconditionFailed.Should().BeFalse();
        (await _http.SendAsync(_account, "GET", href)).Should().Contain("FN:Maxi Mustermann");
    }

    [Fact]
    public async Task Delete_ExistingAndMissingResource_Succeeds()
    {
        var href = await Client().EnsureAddressBookAsync(_account, Ct) + "1.vcf";
        await Client().PutAsync(_account, href, Card, ifMatch: null, unconditional: false, Ct);

        await Client().DeleteAsync(_account, href, Ct);
        await Client().DeleteAsync(_account, href, Ct);

        (await _http.StatusAsync(_account, "GET", href)).Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Plain HTTP access for arranging and verifying; never follows redirects, like production.</summary>
    private sealed class RealHttp : IHttpClientFactory, IDisposable
    {
        private readonly SocketsHttpHandler _handler = new() { AllowAutoRedirect = false };

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);

        public async Task<string> SendAsync(DavAccount account, string method, string href, string? body = null, string? depth = null)
        {
            using var response = await RawAsync(account, method, href, body, depth);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(Ct);
        }

        public async Task<HttpStatusCode> StatusAsync(DavAccount account, string method, string href)
        {
            using var response = await RawAsync(account, method, href, null, null);
            return response.StatusCode;
        }

        public void Dispose() => _handler.Dispose();

        private async Task<HttpResponseMessage> RawAsync(DavAccount account, string method, string href, string? body, string? depth)
        {
            using var client = CreateClient("");
            using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(account.ServerUrl, href));
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{account.UserName}:{account.Password}")));
            if (body is not null)
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/xml");
            }

            if (depth is not null)
            {
                request.Headers.Add("Depth", depth);
            }

            return await client.SendAsync(request, Ct);
        }
    }
}

/// <summary>Credential handling and redirects with a scripted server.</summary>
public sealed class NextcloudDavClientSecurityTests : IDisposable
{
    private static readonly DavAccount Account = new(new Uri("https://cloud.example.test"), "guido", "app-password");

    private readonly StubHttp _http = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private NextcloudDavClient Client() => new(_http, NullLogger<NextcloudDavClient>.Instance);

    public void Dispose() => _http.Dispose();

    private static HttpResponseMessage MultiStatus(string responses) => new(HttpStatusCode.MultiStatus)
    {
        Content = new StringContent($"<?xml version=\"1.0\"?><d:multistatus xmlns:d=\"DAV:\" xmlns:card=\"urn:ietf:params:xml:ns:carddav\">{responses}</d:multistatus>", Encoding.UTF8, "application/xml"),
    };

    private static HttpResponseMessage Redirect(string location) => new(HttpStatusCode.MovedPermanently) { Headers = { Location = new Uri(location, UriKind.RelativeOrAbsolute) } };

    [Fact]
    public async Task Put_ServerAnswers401_ThrowsUnauthorized()
    {
        _http.Respond(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var act = () => Client().PutAsync(Account, "/remote.php/dav/addressbooks/users/guido/solocrm/1.vcf", new DavContent("x", "text/vcard"), null, false, Ct);

        await act.Should().ThrowAsync<DavUnauthorizedException>();
        _http.Requests.Single().Authorization.Should().Be("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("guido:app-password")));
    }

    [Fact]
    public async Task EnsureAddressBook_PlainServerAddress_FollowsRedirectWithinTheServer()
    {
        _http.Respond(_ => Redirect("/remote.php/dav/"));
        _http.Respond(_ => MultiStatus("<d:response><d:href>/remote.php/dav/</d:href><d:propstat><d:prop><d:current-user-principal><d:href>/remote.php/dav/principals/users/guido/</d:href></d:current-user-principal></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>"));
        _http.Respond(_ => MultiStatus("<d:response><d:href>/remote.php/dav/principals/users/guido/</d:href><d:propstat><d:prop><card:addressbook-home-set><d:href>https://cloud.example.test/remote.php/dav/addressbooks/users/guido/</d:href></card:addressbook-home-set></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>"));
        _http.Respond(_ => MultiStatus(
            "<d:response><d:href>/remote.php/dav/addressbooks/users/guido/</d:href><d:propstat><d:prop><d:resourcetype><d:collection/></d:resourcetype></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>"
            + "<d:response><d:href>/remote.php/dav/addressbooks/users/guido/contacts/</d:href><d:propstat><d:prop><d:resourcetype><d:collection/><card:addressbook/></d:resourcetype><d:displayname>Kontakte</d:displayname></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>"
            + "<d:response><d:href>/remote.php/dav/addressbooks/users/guido/solocrm/</d:href><d:propstat><d:prop><d:resourcetype><d:collection/><card:addressbook/></d:resourcetype><d:displayname>SoloCRM</d:displayname></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>"));

        var href = await Client().EnsureAddressBookAsync(Account, Ct);

        href.Should().Be("/remote.php/dav/addressbooks/users/guido/solocrm/");
        _http.Requests.Should().OnlyContain(r => r.Uri.Host == "cloud.example.test" && r.Authorization != null);
        _http.Requests[1].Uri.AbsolutePath.Should().Be("/remote.php/dav/");
    }

    [Fact]
    public async Task EnsureAddressBook_RedirectToOtherHost_NeverSendsTheCredentialsThere()
    {
        _http.Respond(_ => Redirect("https://evil.example.test/remote.php/dav/"));
        _http.Respond(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        _http.Respond(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var act = () => Client().EnsureAddressBookAsync(Account, Ct);

        await act.Should().ThrowAsync<DavException>().WithMessage("*keine Nextcloud*");
        _http.Requests.Should().OnlyContain(r => r.Uri.Host == "cloud.example.test");
    }

    [Fact]
    public async Task EnsureAddressBook_ServerUnreachable_ThrowsWithoutTryingFurther()
    {
        _http.Throw(new HttpRequestException("connection refused"));

        var act = () => Client().EnsureAddressBookAsync(Account, Ct);

        await act.Should().ThrowAsync<DavException>().WithMessage("*nicht erreichbar*");
        _http.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Put_ServerReportsNoETag_ReadsItWithPropFind()
    {
        _http.Respond(_ => new HttpResponseMessage(HttpStatusCode.Created));
        _http.Respond(_ => MultiStatus("<d:response><d:href>/dav/1.vcf</d:href><d:propstat><d:prop><d:getetag>\"e-1\"</d:getetag></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>"));

        var result = await Client().PutAsync(Account, "/dav/1.vcf", new DavContent("x", "text/vcard"), null, false, Ct);

        result.ETag.Should().Be("\"e-1\"");
        _http.Requests.Select(r => r.Method.Method).Should().Equal("PUT", "PROPFIND");
    }
}
