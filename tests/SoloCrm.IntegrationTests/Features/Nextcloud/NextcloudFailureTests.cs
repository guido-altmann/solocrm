using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Nextcloud;
using SoloCrm.Application.Features.Tasks;
using SoloCrm.Domain.Contacts;
using SoloCrm.Domain.Nextcloud;
using SoloCrm.Infrastructure.Nextcloud;
using SoloCrm.Infrastructure.Persistence;

namespace SoloCrm.IntegrationTests.Features.Nextcloud;

/// <summary>Failures of the server and their effect on the stored connection (ADR-014), with a scripted target.</summary>
public sealed class NextcloudFailureTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    private const string ServerUrl = "https://cloud.example.test";

    private readonly FakeTarget _target = new();

    private ConnectNextcloud.Handler ConnectHandler() => new(
        Get<ICrmDbContextFactory>(), _target, Get<ISecretProtector>(), new ConnectNextcloud.Validator(NextcloudServers.HttpsOnly), Time);

    private SyncNextcloud.Handler SyncHandler() => new(
        Get<ICrmDbContextFactory>(),
        _target,
        Get<ISecretProtector>(),
        Get<ICommandHandler<CompleteTask.Command, CompleteTask.Result>>(),
        Get<ICommandHandler<ReopenTask.Command, ReopenTask.Result>>(),
        new NextcloudSyncOptions { AppBaseUrl = "https://crm.example.test" },
        Time);

    private Task<Result<ConnectNextcloud.Result>> ConnectAsync(string user = "guido") =>
        ConnectHandler().Handle(new ConnectNextcloud.Command(ServerUrl, user, "app-password"), Ct);

    private Task<Result<SyncNextcloud.Result>> SyncAsync() => SyncHandler().Handle(new SyncNextcloud.Command(), Ct);

    private async Task AddContactAsync()
    {
        await using var db = OpenDb();
        db.Contacts.Add(Contact.Create("Max", "Mustermann"));
        await db.SaveChangesAsync(Ct);
    }

    private async Task<NextcloudConnection> ConnectionAsync()
    {
        await using var db = OpenDb();
        return await db.NextcloudConnections.SingleAsync(Ct);
    }

    [Fact]
    public async Task Connect_ServerRejectsCredentials_ReturnsFailureAndStoresNothing()
    {
        _target.OnEnsure = () => throw new DavUnauthorizedException();

        var result = await ConnectAsync();

        result.Error.Code.Should().Be("Nextcloud.ConnectFailed");
        result.Error.Message.Should().Contain("App-Passwort");
        await using var db = OpenDb();
        (await db.NextcloudConnections.AnyAsync(Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Sync_ServerRejectsCredentials_PausesUntilNewCredentialsAreSaved()
    {
        await ConnectAsync();
        await AddContactAsync();
        _target.OnPut = () => throw new DavUnauthorizedException();

        var first = await SyncAsync();
        var second = await SyncAsync();

        first.Error.Code.Should().Be("Nextcloud.Unauthorized");
        second.Error.Should().Be(NextcloudErrors.CredentialRejected);
        _target.Puts.Should().Be(1);
        (await ConnectionAsync()).IsCredentialRejected.Should().BeTrue();

        _target.OnPut = null;
        (await ConnectAsync()).IsSuccess.Should().BeTrue();
        var resumed = await SyncAsync();

        resumed.Value.Written.Should().Be(1);
        (await ConnectionAsync()).IsCredentialRejected.Should().BeFalse();
    }

    [Fact]
    public async Task Sync_ServerUnreachable_RecordsErrorAndTriesAgainNextTime()
    {
        await ConnectAsync();
        await AddContactAsync();
        _target.OnPut = () => throw new DavException("Nextcloud ist gerade nicht erreichbar.");

        var failed = await SyncAsync();

        failed.Error.Code.Should().Be("Nextcloud.SyncFailed");
        var connection = await ConnectionAsync();
        connection.LastError.Should().Be("Nextcloud ist gerade nicht erreichbar.");
        connection.IsCredentialRejected.Should().BeFalse();

        _target.OnPut = null;
        (await SyncAsync()).Value.Written.Should().Be(1);
        (await ConnectionAsync()).LastError.Should().BeNull();
    }

    [Fact]
    public async Task Sync_CopyChangedRemotely_OverwritesWithoutCondition()
    {
        await ConnectAsync();
        await AddContactAsync();
        _target.Conflicts = 1;

        var result = await SyncAsync();

        result.Value.Written.Should().Be(1);
        _target.UnconditionalPuts.Should().Be(1);
    }

    [Fact]
    public async Task Sync_TaskChangedRemotelyDuringTheRun_IsSkippedInsteadOfOverwritten()
    {
        await ConnectAsync();
        await SendAsync<CreateTask.Command, CreateTask.Result>(new CreateTask.Command("Anrufen"));
        _target.Conflicts = 1;

        var result = await SyncAsync();

        result.Value.Skipped.Should().Be(1);
        result.Value.Written.Should().Be(0);
        _target.UnconditionalPuts.Should().Be(0);
        await using var db = OpenDb();
        (await db.NextcloudSyncItems.AnyAsync(Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Sync_ExistingConnectionWithoutTaskList_CreatesItOnTheNextRun()
    {
        await using (var db = OpenDb())
        {
            var connection = new NextcloudConnection(ServerUrl, "guido", Get<ISecretProtector>().Protect("app-password"), Start);
            connection.UseAddressBook("/dav/guido/solocrm/");
            db.NextcloudConnections.Add(connection);
            await db.SaveChangesAsync(Ct);
        }

        (await SyncAsync()).IsSuccess.Should().BeTrue();

        var stored = await ConnectionAsync();
        stored.TaskListHref.Should().Be("/dav/guido/solocrm-tasks/");
        stored.TaskSyncToken.Should().Be("token-1");
    }

    [Fact]
    public async Task Connect_OtherAccount_ForgetsWhatWasMirrored()
    {
        await ConnectAsync("guido");
        await AddContactAsync();
        await SyncAsync();

        await ConnectAsync("anna");

        await using var db = OpenDb();
        (await db.NextcloudSyncItems.AnyAsync(Ct)).Should().BeFalse();
        (await db.NextcloudConnections.SingleAsync(Ct)).UserName.Should().Be("anna");
    }

    [Fact]
    public async Task Connect_SameAccountAgain_KeepsTheMapping()
    {
        await ConnectAsync();
        await AddContactAsync();
        await SyncAsync();

        await ConnectAsync();

        (await SyncAsync()).Value.Unchanged.Should().Be(1);
    }

    [Fact]
    public async Task BackgroundService_NotConnectedOrPaused_DoesNotSync()
    {
        var service = new NextcloudSyncBackgroundService(
            CountingServices(out var handler),
            Get<IDbContextFactory<CrmDbContext>>(),
            Options.Create(new NextcloudWorkerOptions()),
            Time,
            NullLogger<NextcloudSyncBackgroundService>.Instance);

        await service.SyncOnceAsync(Ct);
        await ConnectAsync();
        await using (var db = OpenDb())
        {
            (await db.NextcloudConnections.SingleAsync(Ct)).RejectCredentials("abgelehnt");
            await db.SaveChangesAsync(Ct);
        }

        await service.SyncOnceAsync(Ct);

        handler.Calls.Should().Be(0);
    }

    [Fact]
    public async Task BackgroundService_Connected_RunsTheSyncAndSurvivesFailures()
    {
        await ConnectAsync();
        var service = new NextcloudSyncBackgroundService(
            CountingServices(out var handler, Error.Failure("Nextcloud.SyncFailed", "nicht erreichbar")),
            Get<IDbContextFactory<CrmDbContext>>(),
            Options.Create(new NextcloudWorkerOptions()),
            Time,
            NullLogger<NextcloudSyncBackgroundService>.Instance);

        await service.SyncOnceAsync(Ct);
        await service.SyncOnceAsync(Ct);

        handler.Calls.Should().Be(2);
    }

    private static ServiceProvider CountingServices(out CountingHandler handler, Error? error = null)
    {
        handler = new CountingHandler(error);
        var services = new ServiceCollection();
        services.AddSingleton<ICommandHandler<SyncNextcloud.Command, SyncNextcloud.Result>>(handler);
        return services.BuildServiceProvider();
    }

    private sealed class CountingHandler(Error? error) : ICommandHandler<SyncNextcloud.Command, SyncNextcloud.Result>
    {
        public int Calls { get; private set; }

        public Task<Result<SyncNextcloud.Result>> Handle(SyncNextcloud.Command command, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(error is null ? Result<SyncNextcloud.Result>.Success(new(0, 0, 0, 0, 0, 0)) : Result<SyncNextcloud.Result>.Failure(error));
        }
    }

    private sealed class FakeTarget : IDavSyncTarget
    {
        public Func<string>? OnEnsure { get; set; }

        public Action? OnPut { get; set; }

        /// <summary>Number of conditional writes still answered with 412.</summary>
        public int Conflicts { get; set; }

        public int Puts { get; private set; }

        public int UnconditionalPuts { get; private set; }

        public Task<string> EnsureAddressBookAsync(DavAccount account, CancellationToken cancellationToken) =>
            Task.FromResult(OnEnsure?.Invoke() ?? $"/dav/{account.UserName}/solocrm/");

        public Task<string> EnsureTaskListAsync(DavAccount account, CancellationToken cancellationToken) =>
            Task.FromResult($"/dav/{account.UserName}/solocrm-tasks/");

        public Task<DavChanges> GetChangesAsync(DavAccount account, string collectionHref, string? syncToken, CancellationToken cancellationToken) =>
            Task.FromResult(new DavChanges([], "token-1"));

        public Task<string?> GetAsync(DavAccount account, string href, CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public Task<DavWriteResult> PutAsync(
            DavAccount account, string href, DavContent content, string? ifMatch, bool unconditional, CancellationToken cancellationToken)
        {
            Puts++;
            OnPut?.Invoke();
            if (unconditional)
            {
                UnconditionalPuts++;
            }
            else if (Conflicts > 0)
            {
                Conflicts--;
                return Task.FromResult(DavWriteResult.Conflict);
            }

            return Task.FromResult(DavWriteResult.Written($"\"e-{Puts}\""));
        }

        public Task DeleteAsync(DavAccount account, string href, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
