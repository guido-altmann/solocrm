using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Mail;
using SoloCrm.Infrastructure.Mail;

namespace SoloCrm.IntegrationTests.Mail;

public sealed class MailSyncBackgroundServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MailSyncBackgroundService CreateService(FakeConnector connector, FakeHandler handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICommandHandler<SyncMail.Command, SyncMail.Result>>(handler);
        return new MailSyncBackgroundService(
            services.BuildServiceProvider(),
            connector,
            Options.Create(new MailWorkerOptions()),
            TimeProvider.System,
            NullLogger<MailSyncBackgroundService>.Instance);
    }

    [Fact]
    public async Task SyncOnce_NoMailboxConnected_DoesNotSync()
    {
        var connector = new FakeConnector(connected: false);
        var handler = new FakeHandler(new SyncMail.Result(0, 0, 0));

        await CreateService(connector, handler).SyncOnceAsync(Ct);

        handler.Calls.Should().Be(0);
        connector.Recorded.Should().BeEmpty();
    }

    [Fact]
    public async Task SyncOnce_Success_RecordsSuccess()
    {
        var connector = new FakeConnector(connected: true);
        var handler = new FakeHandler(new SyncMail.Result(2, 0, 1));

        await CreateService(connector, handler).SyncOnceAsync(Ct);

        handler.Calls.Should().Be(1);
        connector.Recorded.Should().Equal((string?)null);
    }

    [Fact]
    public async Task SyncOnce_Failure_RecordsTheUserFacingMessage()
    {
        var connector = new FakeConnector(connected: true);
        var handler = new FakeHandler(Error.Failure("Mail.SourceFailed", "Das Postfach konnte nicht gelesen werden: Anmeldung abgelaufen."));

        await CreateService(connector, handler).SyncOnceAsync(Ct);

        connector.Recorded.Should().Equal("Das Postfach konnte nicht gelesen werden: Anmeldung abgelaufen.");
    }

    private sealed class FakeHandler : ICommandHandler<SyncMail.Command, SyncMail.Result>
    {
        private readonly Result<SyncMail.Result> _result;

        public FakeHandler(Result<SyncMail.Result> result) => _result = result;

        public int Calls { get; private set; }

        public Task<Result<SyncMail.Result>> Handle(SyncMail.Command command, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeConnector(bool connected) : IMailConnector
    {
        public List<string?> Recorded { get; } = [];

        public bool IsConfigured => true;

        public Uri BuildAuthorizationUri(string redirectUri, string state, string codeChallenge) => throw new NotSupportedException();

        public Task<string> ConnectAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<MailConnectionInfo?> GetConnectionAsync(CancellationToken cancellationToken) =>
            Task.FromResult(connected ? new MailConnectionInfo("Graph", "guido@example.test", DateTimeOffset.UnixEpoch, null, null) : null);

        public Task RecordSyncAsync(string? error, CancellationToken cancellationToken)
        {
            Recorded.Add(error);
            return Task.CompletedTask;
        }
    }
}
