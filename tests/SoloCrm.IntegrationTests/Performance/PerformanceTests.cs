using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Features.Timeline;
using SoloCrm.Application.Features.Today;
using SoloCrm.IntegrationTests.Features;

namespace SoloCrm.IntegrationTests.Performance;

/// <summary>
/// NFR (SPEC 6): timeline and „Heute“ below 200 ms with 10k contacts and 50k activities. Measured as the median
/// of several warm runs, so a single slow run on a busy CI machine does not fail the build.
/// </summary>
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
