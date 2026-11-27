using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Enrichment;
using SoloCrm.Domain.Enrichment;
using SoloCrm.Domain.Organizations;
using SoloCrm.IntegrationTests.Features;

namespace SoloCrm.IntegrationTests.Enrichment;

public class RequestEnrichmentTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    protected override IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["Ai:ProjectEndpoint"] = "https://foundry.example.test/api/projects/crm",
        ["Ai:AgentName"] = "company-enrichment",
        ["Enrichment:MaxRequestsPerDay"] = "2",
    };

    private async Task<Organization> AddOrganizationAsync(string name = "Beispiel GmbH", bool archived = false)
    {
        var organization = Organization.Create(name);
        if (archived)
        {
            organization.Archive();
        }

        await using var db = OpenDb();
        db.Organizations.Add(organization);
        await db.SaveChangesAsync(Ct);
        return organization;
    }

    private Task<Result<RequestEnrichment.Result>> RequestAsync(Guid organizationId) =>
        SendAsync<RequestEnrichment.Command, RequestEnrichment.Result>(new RequestEnrichment.Command(organizationId));

    [Fact]
    public async Task Handle_Organization_QueuesAPendingRequest()
    {
        var organization = await AddOrganizationAsync();

        var result = await RequestAsync(organization.Id);

        result.IsSuccess.Should().BeTrue();
        await using var db = OpenDb();
        var request = await db.EnrichmentRequests.SingleAsync(r => r.Id == result.Value.RequestId, Ct);
        request.Status.Should().Be(EnrichmentRequestStatus.Pending);
        request.TargetType.Should().Be(EnrichmentTargetType.Organization);
        request.TargetId.Should().Be(organization.Id);
        request.Provider.Should().Be("FoundryAgent");
        request.RequestedAt.Should().Be(Start);
    }

    [Fact]
    public async Task Handle_OrganizationWithoutWebsite_IsAccepted()
    {
        var organization = await AddOrganizationAsync();

        (await RequestAsync(organization.Id)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_EmptyId_ReturnsValidationError()
    {
        var result = await RequestAsync(Guid.Empty);

        result.Error.Should().BeOfType<ValidationError>();
    }

    [Fact]
    public async Task Handle_UnknownOrganization_ReturnsNotFound()
    {
        var result = await RequestAsync(Guid.CreateVersion7());

        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Handle_ArchivedOrganization_ReturnsConflict()
    {
        var organization = await AddOrganizationAsync(archived: true);

        var result = await RequestAsync(organization.Id);

        result.Error.Should().Be(RequestEnrichment.Archived);
    }

    [Fact]
    public async Task Handle_ActiveRequestExists_ReturnsConflict()
    {
        var organization = await AddOrganizationAsync();
        await RequestAsync(organization.Id);

        var result = await RequestAsync(organization.Id);

        result.Error.Should().Be(RequestEnrichment.AlreadyActive);
    }

    [Fact]
    public async Task Handle_EarlierRequestFinished_QueuesANewOne()
    {
        var organization = await AddOrganizationAsync();
        var first = await RequestAsync(organization.Id);
        await FinishAsync(first.Value.RequestId);

        var result = await RequestAsync(organization.Id);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_TwoRequestsAtOnce_QueuesExactlyOne()
    {
        var organization = await AddOrganizationAsync();

        var results = await Task.WhenAll(RequestAsync(organization.Id), RequestAsync(organization.Id));

        results.Count(r => r.IsSuccess).Should().Be(1);
        results.Single(r => r.IsFailure).Error.Should().Be(RequestEnrichment.AlreadyActive);
    }

    [Fact]
    public async Task Handle_DailyLimitReached_ReturnsErrorUntilTheNextDay()
    {
        var first = await AddOrganizationAsync("Eins");
        var second = await AddOrganizationAsync("Zwei");
        var third = await AddOrganizationAsync("Drei");
        await FinishAsync((await RequestAsync(first.Id)).Value.RequestId);
        await FinishAsync((await RequestAsync(second.Id)).Value.RequestId);

        var rejected = await RequestAsync(third.Id);
        Time.Advance(TimeSpan.FromDays(1));
        var nextDay = await RequestAsync(third.Id);

        rejected.Error.Code.Should().Be("Enrichment.DailyLimit");
        rejected.Error.Message.Should().Contain("2");
        nextDay.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_FailedRequestsCountTowardsTheLimit()
    {
        var organization = await AddOrganizationAsync();
        var other = await AddOrganizationAsync("Zwei");
        var first = await RequestAsync(organization.Id);
        await FinishAsync(first.Value.RequestId, fail: true);
        await FinishAsync((await RequestAsync(organization.Id)).Value.RequestId, fail: true);

        var result = await RequestAsync(other.Id);

        result.Error.Code.Should().Be("Enrichment.DailyLimit");
    }

    private async Task FinishAsync(Guid requestId, bool fail = false)
    {
        await using var db = OpenDb();
        var request = await db.EnrichmentRequests.SingleAsync(r => r.Id == requestId, Ct);
        request.Claim(Start, Start.AddMinutes(5));
        if (fail)
        {
            request.Fail(Start, "Fehler");
        }
        else
        {
            request.Complete(Start);
        }

        await db.SaveChangesAsync(Ct);
    }
}

public sealed class RequestEnrichmentNotConfiguredTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    [Fact]
    public async Task Handle_AgentNotConfigured_ReturnsNotConfiguredAndQueuesNothing()
    {
        var organization = Organization.Create("Beispiel GmbH");
        await using (var db = OpenDb())
        {
            db.Organizations.Add(organization);
            await db.SaveChangesAsync(Ct);
        }

        var result = await SendAsync<RequestEnrichment.Command, RequestEnrichment.Result>(new RequestEnrichment.Command(organization.Id));

        result.Error.Should().Be(RequestEnrichment.NotConfigured);
        await using var verify = OpenDb();
        (await verify.EnrichmentRequests.CountAsync(Ct)).Should().Be(0);
    }
}
