using System.Text.Json;
using SoloCrm.Application.Features.Contacts;
using SoloCrm.Domain.Activities;
using SoloCrm.Domain.Auditing;
using SoloCrm.Domain.Common;
using SoloCrm.Domain.Opportunities;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Application.Tests.Features.Contacts;

/// <summary>The documented export format (US-19, formatVersion 1).</summary>
public sealed class ExportContactDataFormatTests
{
    private static readonly Guid Id = Guid.Parse("01999999-0000-7000-8000-000000000001");
    private static readonly DateTimeOffset Berlin = new(2026, 10, 2, 10, 15, 0, TimeSpan.FromHours(2));

    [Fact]
    public void ToJson_Export_WritesTopLevelSectionsInCamelCase()
    {
        using var json = JsonDocument.Parse(ExportContactData.ToJson(Export()));

        json.RootElement.EnumerateObject().Select(p => p.Name).Should()
            .Equal("formatVersion", "exportedAt", "contact", "activities", "tasks", "opportunities", "auditEntries");
    }

    [Fact]
    public void ToJson_TimestampsWithOffset_WritesUtcWithZ()
    {
        using var json = JsonDocument.Parse(ExportContactData.ToJson(Export()));

        json.RootElement.GetProperty("exportedAt").GetString().Should().Be("2026-10-02T08:15:00.000Z");
        json.RootElement.GetProperty("activities")[0].GetProperty("occurredAt").GetString().Should().Be("2026-10-02T08:15:00.000Z");
        json.RootElement.GetProperty("tasks")[0].GetProperty("dueDate").GetString().Should().Be("2026-10-05");
    }

    [Fact]
    public void ToJson_Enums_WritesCamelCaseNames()
    {
        using var json = JsonDocument.Parse(ExportContactData.ToJson(Export()));

        var root = json.RootElement;
        root.GetProperty("contact").GetProperty("source").GetString().Should().Be("linkedIn");
        root.GetProperty("contact").GetProperty("organization").GetProperty("type").GetString().Should().Be("agency");
        root.GetProperty("opportunities")[0].GetProperty("stageStatus").GetString().Should().Be("open");
        root.GetProperty("opportunities")[0].GetProperty("role").GetString().Should().Be("primaryContact");
        root.GetProperty("auditEntries")[0].GetProperty("action").GetString().Should().Be("created");
    }

    [Fact]
    public void ToJson_Umlauts_StayReadable()
    {
        var json = ExportContactData.ToJson(Export());

        json.Should().Contain("\"lastName\": \"Müller\"").And.Contain("\"extraFields\": {");
    }

    private static ExportContactData.Result Export() => new(
        ExportContactData.FormatVersion,
        Berlin,
        new ExportContactData.ContactData(
            Id, "Jürgen", "Müller", "j@example.test", null, "CTO", null, LeadSource.LinkedIn, false,
            new ExportContactData.AddressExport("Hauptstr. 1", null, "10115", "Berlin", null, "DE"),
            new Dictionary<string, string> { ["HubSpotRecordId"] = "4711" },
            new ExportContactData.OrganizationData(Id, "Contoso", OrganizationType.Agency),
            ["VIP"],
            Berlin,
            Berlin),
        [new ExportContactData.ActivityData(Id, ActivityType.Call, Berlin, null, "Telefonat", null, null, Berlin, Berlin)],
        [new ExportContactData.TaskData(Id, "Rückruf", new DateOnly(2026, 10, 5), null, null, null, Berlin, Berlin)],
        [new ExportContactData.OpportunityData(Id, "Migration", "Beworben", StageStatus.Open, ExportContactData.ContactRole.PrimaryContact, Berlin)],
        [new ExportContactData.AuditEntryData(Id, "Contact", Id, AuditAction.Created, Berlin, [new AuditChange("LastName", null, "Müller")])]);
}
