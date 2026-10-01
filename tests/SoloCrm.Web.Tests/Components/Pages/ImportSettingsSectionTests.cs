using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Import;
using SoloCrm.Web.Components.Pages.Settings;

namespace SoloCrm.Web.Tests.Components.Pages;

public sealed class ImportSettingsSectionTests : BunitContext
{
    private const string HubSpotCsv =
        "\"Record ID\",\"First Name\",\"Last Name\",\"Email\",\"Work email\",\"Lifecycle Stage\",\"Billing Contact IDs\",\"Billing Contact IDs\"\n"
        + "\"101\",\"Ada\",\"Lovelace\",\"\",\"ada@work.test\",\"Kunde\",\"1\",\"2\"\n"
        + "\"102\",\"Grace\",\"Hopper\",\"grace@example.test\",\"\",\"Lead\",\"\",\"\"\n";

    private readonly ICommandHandler<ImportContacts.Command, ImportContacts.Result> _import =
        Substitute.For<ICommandHandler<ImportContacts.Command, ImportContacts.Result>>();

    private readonly ICommandHandler<ImportOrganizations.Command, ImportOrganizations.Result> _importOrganizations =
        Substitute.For<ICommandHandler<ImportOrganizations.Command, ImportOrganizations.Result>>();

    public ImportSettingsSectionTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<IQueryHandler<PreviewImport.Query, PreviewImport.Result>>(
            new PreviewImport.Handler(new PreviewImport.Validator()));
        Services.AddSingleton(_import);
        Services.AddSingleton(_importOrganizations);
    }

    [Fact]
    public void Upload_HubSpotExport_SuggestsTemplateAndPreviewsMappedColumnsWithFallback()
    {
        var section = RenderSection();

        Upload(section, HubSpotCsv);

        section.WaitForAssertion(() => section.Find("[data-test=file-info]").TextContent.Should().Contain("kontakte.csv").And.Contain("2 Zeilen"));
        section.Markup.Should().Contain("HubSpot-Kontaktexport");
        var preview = section.Find(".import-preview");
        preview.TextContent.Should().Contain("ada@work.test", "the work email fills the empty email");
        preview.TextContent.Should().Contain("Kunde");
        preview.TextContent.Should().NotContain("Billing", "only mapped columns are previewed");
    }

    [Fact]
    public async Task Import_Report_ShowsCountsAndRowsWithReasons()
    {
        _import.Handle(Arg.Any<ImportContacts.Command>(), Arg.Any<CancellationToken>())
            .Returns(Result<ImportContacts.Result>.Success(new ImportContacts.Result(new ImportReport(
                1, 0, 1, 1,
                [new ImportRowIssue(3, "Kontakt existiert bereits (E-Mail).")],
                [new ImportRowIssue(2, "Bitte eine gültige E-Mail-Adresse angeben.")]))));
        var section = RenderSection();
        Upload(section, HubSpotCsv);
        section.WaitForAssertion(() => section.FindAll("button").Should().Contain(b => b.TextContent.Contains("importieren", StringComparison.Ordinal)));

        await section.FindAll("button").Single(b => b.TextContent.Contains("2 Zeilen importieren", StringComparison.Ordinal)).ClickAsync(new());

        section.WaitForAssertion(() => section.Find("[data-test=import-report]").TextContent
            .Should().Contain("1 angelegt").And.Contain("1 übersprungen").And.Contain("1 fehlerhaft")
            .And.Contain("Bitte eine gültige E-Mail-Adresse angeben.").And.Contain("Kontakt existiert bereits (E-Mail)."));
        await _import.Received(1).Handle(
            Arg.Is<ImportContacts.Command>(c =>
                c.Mapping.Template == ImportTemplate.HubSpot
                && c.Duplicates == DuplicateHandling.Skip
                && c.Mapping.For(ImportField.Email)!.FallbackColumn == 4
                && c.Content.Length == Encoding.UTF8.GetByteCount(HubSpotCsv)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Upload_FileWithoutHeader_ShowsErrorAndStaysOnUpload()
    {
        var section = RenderSection();

        Upload(section, "\n\n");

        section.WaitForAssertion(() => section.Markup.Should().Contain(ImportErrors.EmptyFile.Message));
        section.FindAll("button").Should().Contain(b => b.TextContent.Contains("CSV-Datei wählen", StringComparison.Ordinal));
    }

    [Fact]
    public void Upload_SemicolonFile_UsesGenericTemplate()
    {
        var section = RenderSection();
        Upload(section, "Vorname;Nachname;Firma\nAda;Lovelace;Contoso\n");

        section.WaitForAssertion(() => section.Find(".import-preview").TextContent.Should().Contain("Contoso"));

        section.Markup.Should().Contain("Allgemein");
        section.Find("[data-test=file-info]").TextContent.Should().Contain("„;“");
    }

    [Fact]
    public async Task Upload_HubSpotCompanyExport_ImportsOrganizationsWithOrganizationFields()
    {
        const string companies =
            "\"Record ID\",\"Company name\",\"Company Domain Name\",\"Type\",\"City\",\"Country/Region Code\"\n"
            + "\"9001\",\"Contoso GmbH\",\"contoso.de\",\"Prospect\",\"Berlin\",\"DE\"\n";
        _importOrganizations.Handle(Arg.Any<ImportOrganizations.Command>(), Arg.Any<CancellationToken>())
            .Returns(Result<ImportOrganizations.Result>.Success(new ImportOrganizations.Result(new ImportReport(1, 0, 0, 0, [], []))));
        var section = RenderSection();

        Upload(section, companies);

        section.WaitForAssertion(() => section.Markup.Should().Contain("HubSpot-Firmenexport"));
        section.FindAll("[data-field]").Select(r => r.GetAttribute("data-field")).Should().Contain(["Organization", "OrganizationType"])
            .And.NotContain(["FirstName", "HubSpotCompanyId"]);
        section.Find(".import-preview").TextContent.Should().Contain("Contoso GmbH").And.Contain("contoso.de");

        await section.FindAll("button").Single(b => b.TextContent.Contains("1 Zeile importieren", StringComparison.Ordinal)).ClickAsync(new());

        section.WaitForAssertion(() => section.Find("[data-test=import-report]").TextContent.Should().Contain("1 angelegt"));
        await _importOrganizations.Received(1).Handle(
            Arg.Is<ImportOrganizations.Command>(c => c.Mapping.Target == ImportTarget.Organizations && c.Mapping.Template == ImportTemplate.HubSpot),
            Arg.Any<CancellationToken>());
        await _import.DidNotReceiveWithAnyArgs().Handle(default!, Xunit.TestContext.Current.CancellationToken);
    }

    private IRenderedComponent<ImportSettingsSection> RenderSection()
    {
        Render<MudPopoverProvider>();
        return Render<ImportSettingsSection>();
    }

    private static void Upload(IRenderedComponent<ImportSettingsSection> section, string csv) =>
        section.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText(csv, "kontakte.csv"));
}
