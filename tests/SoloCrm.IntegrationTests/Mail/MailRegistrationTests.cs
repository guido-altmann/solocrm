using SoloCrm.Application.Abstractions;
using SoloCrm.IntegrationTests.Features;

namespace SoloCrm.IntegrationTests.Mail;

public sealed class MailRegistrationTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    protected override IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["Mail:Graph:TenantId"] = "tenant-1.example.test",
        ["Mail:Graph:ClientId"] = "client-1",
        ["Mail:Graph:ClientSecret"] = "secret-1",
        ["Mail:BackfillDays"] = "7",
    };

    [Fact]
    public void Services_GraphConfigured_UseGraphSourceAndConnector()
    {
        Get<IMailSource>().Provider.Should().Be("Graph");
        Get<IMailConnector>().IsConfigured.Should().BeTrue();
        Get<SoloCrm.Application.Features.Mail.MailSyncOptions>().BackfillDays.Should().Be(7);
    }

    [Fact]
    public async Task Sync_GraphConfiguredButNotConnected_ReportsNoMailbox()
    {
        var result = await SendAsync<SoloCrm.Application.Features.Mail.SyncMail.Command, SoloCrm.Application.Features.Mail.SyncMail.Result>(
            new SoloCrm.Application.Features.Mail.SyncMail.Command());

        result.Error.Message.Should().Contain("kein Postfach verbunden");
    }
}
