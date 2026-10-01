using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Features.Contacts;
using SoloCrm.Application.Features.Organizations;
using SoloCrm.Application.Features.Timeline;
using SoloCrm.Application.Features.Today;
using SoloCrm.IntegrationTests.Features;
using SearchFeature = SoloCrm.Application.Features.Search.Search;

namespace SoloCrm.IntegrationTests.Performance;

/// <summary>
/// NFR (SPEC 6): timeline, „Heute“ and search below 200 ms with 10k contacts and 50k activities. Measured as the median
/// of several warm runs, so a single slow run on a busy CI machine does not fail the build.
/// </summary>
[Collection(PerformanceRuns.Name)]
public sealed class PerformanceTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(200);

    [Fact]
    public async Task GetTimelineAndToday_LargeDataset_StayWithinBudget()
    {
        await using (var db = OpenDb())
        {
            await LargeDataset.SeedAsync(db, Ct);
        }

        Guid organizationId;
        Guid contactId;
        await using (var db = OpenDb())
        {
            (await db.Activities.CountAsync(Ct)).Should().BeGreaterThanOrEqualTo(LargeDataset.Activities);
            organizationId = await db.Organizations.OrderBy(o => o.Id).Select(o => o.Id).FirstAsync(Ct);
            contactId = await db.Contacts.Where(c => c.OrganizationId == organizationId).Select(c => c.Id).FirstAsync(Ct);
        }

        var organization = await MeasureAsync(() => QueryAsync<GetTimeline.Query, GetTimeline.Result>(
            new GetTimeline.Query(TimelineRecordType.Organization, organizationId)));
        var contact = await MeasureAsync(() => QueryAsync<GetTimeline.Query, GetTimeline.Result>(
            new GetTimeline.Query(TimelineRecordType.Contact, contactId)));

        var today = await MeasureAsync(() => QueryAsync<GetToday.Query, GetToday.Result>(new GetToday.Query()));

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Timeline organization: {organization.Median.TotalMilliseconds:F1} ms, contact: {contact.Median.TotalMilliseconds:F1} ms, "
            + $"Heute: {today.Median.TotalMilliseconds:F1} ms");
        organization.Result.Value.Entries.Should().HaveCount(GetTimeline.DefaultLimit);
        organization.Median.Should().BeLessThan(Budget);
        contact.Median.Should().BeLessThan(Budget);
        today.Result.Value.StaleOpportunities.Should().NotBeEmpty();
        today.Median.Should().BeLessThan(Budget);
    }

    [Fact]
    public async Task Search_LargeDataset_StaysWithinBudget()
    {
        await using (var db = OpenDb())
        {
            await LargeDataset.SeedAsync(db, Ct);
        }

        // Contact 1234 of the dataset is „Emma Feldkamsch“; „Anna“ is the first name of 333 contacts.
        var cases = new (string Name, Func<Task<object>> Run)[]
        {
            ("palette, name", async () => await Search("Emma Feldkamsch")),
            ("palette, typo", async () => await Search("Feldkamsh")),
            ("palette, short", async () => await Search("an")),
            ("palette, organization", async () => await Search("Kamlin GmbH")),
            ("contacts, common first name", async () => await QueryAsync<GetContacts.Query, GetContacts.Result>(new GetContacts.Query("anna"))),
            ("contacts, short", async () => await QueryAsync<GetContacts.Query, GetContacts.Result>(new GetContacts.Query("s"))),
            ("organizations, search", async () => await QueryAsync<GetOrganizations.Query, GetOrganizations.Result>(new GetOrganizations.Query("hofmann"))),
            ("organizations, autocomplete", async () => await QueryAsync<SearchOrganizations.Query, SearchOrganizations.Result>(new SearchOrganizations.Query("kam"))),
        };

        foreach (var (name, run) in cases)
        {
            var measured = await MeasureAsync(run);
            TestContext.Current.TestOutputHelper?.WriteLine($"Search {name}: {measured.Median.TotalMilliseconds:F1} ms");
            measured.Median.Should().BeLessThan(Budget, name);
        }

        (await Search("Feldkamsh")).Contacts.Select(c => c.Name).Should().Contain("Emma Feldkamsch", "the typo is tolerated");
    }

    private async Task<SearchFeature.Result> Search(string text) =>
        (await QueryAsync<SearchFeature.Query, SearchFeature.Result>(new SearchFeature.Query(text))).Value;

    private static async Task<(T Result, TimeSpan Median)> MeasureAsync<T>(Func<Task<T>> action)
    {
        await action();
        await action();

        var durations = new List<TimeSpan>();
        T result = default!;
        for (var i = 0; i < 5; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            result = await action();
            durations.Add(stopwatch.Elapsed);
        }

        return (result, durations.Order().ElementAt(durations.Count / 2));
    }
}
