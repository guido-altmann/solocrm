using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Mail;
using SoloCrm.Infrastructure.Mail;

namespace SoloCrm.IntegrationTests.Mail;

public sealed class GraphMailSourceTests : IDisposable
{
    private const string DeltaLink = "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages/delta?$deltatoken=abc";
    private static readonly DateTimeOffset Since = new(2026, 9, 14, 10, 0, 0, TimeSpan.FromHours(2));

    private readonly StubHttp _http = new();

    public void Dispose() => _http.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private GraphMailSource CreateSource() => new(_http, new FixedToken(), NullLogger<GraphMailSource>.Instance);

    [Fact]
    public async Task GetChanges_FirstInboxRequest_UsesDeltaEndpointWithFilterAndAuthorization()
    {
        _http.Respond(HttpStatusCode.OK, """{ "value": [], "@odata.deltaLink": "https://graph.microsoft.com/v1.0/x" }""");

        await CreateSource().GetChangesAsync(MailFolder.Inbox, null, Since, Ct);

        var request = _http.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.Uri.Host.Should().Be("graph.microsoft.com");
        request.Uri.AbsolutePath.Should().Be("/v1.0/me/mailFolders/inbox/messages/delta");
        request.Uri.AbsoluteUri.Should().Contain("$select=id,internetMessageId,subject,from,toRecipients,ccRecipients,receivedDateTime,sentDateTime,body,isDraft");
        request.Uri.AbsoluteUri.Should().Contain("$filter=receivedDateTime%20ge%202026-09-14T08%3A00%3A00Z");
        request.Authorization.Should().Be("Bearer token-1");
        request.Prefer.Should().Be("odata.maxpagesize=50");
    }

    [Fact]
    public async Task GetChanges_SentFolder_UsesSentItemsAndSentTime()
    {
        _http.Respond(HttpStatusCode.OK, """
            { "value": [ { "id": "s1", "internetMessageId": "<s1@x>", "receivedDateTime": "2026-10-01T10:00:00Z", "sentDateTime": "2026-10-01T09:59:00Z",
                           "body": { "contentType": "text", "content": "Hi" } } ],
              "@odata.deltaLink": "https://graph.microsoft.com/v1.0/x" }
            """);

        var batch = await CreateSource().GetChangesAsync(MailFolder.Sent, null, Since, Ct);

        _http.Requests.Single().Uri.AbsolutePath.Should().Be("/v1.0/me/mailFolders/sentitems/messages/delta");
        batch.Messages.Should().ContainSingle().Which.OccurredAt.Should().Be(new DateTimeOffset(2026, 10, 1, 9, 59, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task GetChanges_Messages_AreMappedAndFiltered()
    {
        _http.Respond(HttpStatusCode.OK, """
            {
              "value": [
                { "id": "m1", "internetMessageId": "<m1@x>", "subject": "Angebot", "receivedDateTime": "2026-10-01T10:00:00Z",
                  "from": { "emailAddress": { "name": "Max", "address": "max@example.test" } },
                  "toRecipients": [ { "emailAddress": { "address": "guido@example.test" } }, { "emailAddress": { "address": "/O=EXCHANGELABS/CN=RECIPIENTS/CN=abc" } } ],
                  "ccRecipients": [ { "emailAddress": { "address": "cc@example.test" } } ],
                  "body": { "contentType": "html", "content": "<p>Hallo</p>" } },
                { "id": "m2", "receivedDateTime": "2026-10-01T11:00:00Z", "body": { "contentType": "text", "content": "Text" } },
                { "id": "gone", "@removed": { "reason": "deleted" } },
                { "id": "draft", "isDraft": true, "receivedDateTime": "2026-10-01T12:00:00Z" },
                { "id": "nodate" }
              ],
              "@odata.nextLink": "https://graph.microsoft.com/v1.0/next?$skiptoken=1"
            }
            """);

        var batch = await CreateSource().GetChangesAsync(MailFolder.Inbox, null, Since, Ct);

        batch.NextPageToken.Should().Be("https://graph.microsoft.com/v1.0/next?$skiptoken=1");
        batch.DeltaToken.Should().BeNull();
        batch.Messages.Should().HaveCount(2);
        var first = batch.Messages[0];
        first.ExternalId.Should().Be("m1");
        first.InternetMessageId.Should().Be("<m1@x>");
        first.Folder.Should().Be(MailFolder.Inbox);
        first.Sender.Should().Be("max@example.test");
        first.Recipients.Should().Equal("guido@example.test", "cc@example.test");
        first.Subject.Should().Be("Angebot");
        first.OccurredAt.Should().Be(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero));
        first.Body.Should().Be("<p>Hallo</p>");
        first.BodyIsHtml.Should().BeTrue();
        var second = batch.Messages[1];
        second.Sender.Should().BeNull();
        second.Recipients.Should().BeEmpty();
        second.BodyIsHtml.Should().BeFalse();
        second.InternetMessageId.Should().BeNull();
    }

    [Fact]
    public async Task GetChanges_LastPage_ReturnsDeltaLink()
    {
        _http.Respond(HttpStatusCode.OK, $$"""{ "value": [], "@odata.deltaLink": "{{DeltaLink}}" }""");

        var batch = await CreateSource().GetChangesAsync(MailFolder.Inbox, null, Since, Ct);

        batch.NextPageToken.Should().BeNull();
        batch.DeltaToken.Should().Be(DeltaLink);
    }

    [Fact]
    public async Task GetChanges_WithContinuation_RequestsThatLinkWithoutFilter()
    {
        _http.Respond(HttpStatusCode.OK, """{ "value": [], "@odata.deltaLink": "https://graph.microsoft.com/v1.0/y" }""");

        await CreateSource().GetChangesAsync(MailFolder.Inbox, DeltaLink, Since, Ct);

        _http.Requests.Single().Uri.AbsoluteUri.Should().Be(DeltaLink);
    }

    [Theory]
    [InlineData("https://evil.example/v1.0/me/messages/delta")]
    [InlineData("http://graph.microsoft.com/v1.0/me/messages/delta")]
    [InlineData("https://graph.microsoft.com.evil.example/v1.0/x")]
    [InlineData("https://graph.microsoft.com:8443/v1.0/x")]
    [InlineData("not a url")]
    public async Task GetChanges_ContinuationOutsideGraph_ThrowsExpiredWithoutSendingTheToken(string continuation)
    {
        var act = () => CreateSource().GetChangesAsync(MailFolder.Inbox, continuation, null, Ct);

        await act.Should().ThrowAsync<MailDeltaExpiredException>();
        _http.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetChanges_Gone_ThrowsDeltaExpired()
    {
        _http.Respond(HttpStatusCode.Gone, """{ "error": { "code": "syncStateNotFound" } }""");

        var act = () => CreateSource().GetChangesAsync(MailFolder.Inbox, DeltaLink, null, Ct);

        await act.Should().ThrowAsync<MailDeltaExpiredException>();
    }

    [Fact]
    public async Task GetChanges_BadRequestSyncStateInvalid_ThrowsDeltaExpired()
    {
        _http.Respond(HttpStatusCode.BadRequest, """{ "error": { "code": "syncStateInvalid" } }""");

        var act = () => CreateSource().GetChangesAsync(MailFolder.Inbox, DeltaLink, null, Ct);

        await act.Should().ThrowAsync<MailDeltaExpiredException>();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "401")]
    [InlineData(HttpStatusCode.Forbidden, "403")]
    [InlineData(HttpStatusCode.TooManyRequests, "429")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "503")]
    [InlineData(HttpStatusCode.InternalServerError, "500")]
    [InlineData(HttpStatusCode.BadRequest, "400")]
    public async Task GetChanges_ErrorStatus_ThrowsSourceExceptionWithStatus(HttpStatusCode status, string code)
    {
        _http.Respond(status, """{ "error": { "code": "other" } }""");

        var act = () => CreateSource().GetChangesAsync(MailFolder.Inbox, null, Since, Ct);

        var exception = (await act.Should().ThrowAsync<MailSourceException>()).Which;
        exception.Should().NotBeOfType<MailDeltaExpiredException>();
        exception.Message.Should().Contain(code);
    }

    [Fact]
    public async Task GetChanges_NetworkError_ThrowsSourceException()
    {
        _http.Throw(new HttpRequestException("boom"));

        var act = () => CreateSource().GetChangesAsync(MailFolder.Inbox, null, Since, Ct);

        (await act.Should().ThrowAsync<MailSourceException>()).Which.Message.Should().Contain("nicht erreichbar");
    }

    [Fact]
    public async Task GetChanges_UnreadableJson_ThrowsSourceException()
    {
        _http.Respond(HttpStatusCode.OK, "<html>nope</html>");

        var act = () => CreateSource().GetChangesAsync(MailFolder.Inbox, null, Since, Ct);

        await act.Should().ThrowAsync<MailSourceException>();
    }

    private sealed class FixedToken : IGraphAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) => Task.FromResult("token-1");
    }
}
