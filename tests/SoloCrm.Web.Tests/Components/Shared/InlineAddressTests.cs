using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Common;
using SoloCrm.Web.Components.Shared;

namespace SoloCrm.Web.Tests.Components.Shared;

public sealed class InlineAddressTests : BunitContext
{
    public InlineAddressTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Render<MudPopoverProvider>();
    }

    [Fact]
    public void Render_Address_ShowsLinesWithGermanCountryName()
    {
        var field = Render<InlineAddress>(p => p
            .Add(a => a.Value, new AddressData("Hauptstr. 1", null, "10115", "Berlin", null, "DE"))
            .Add(a => a.Save, _ => Task.FromResult<Error?>(null)));

        field.Find(".inline-field-value").InnerHtml.Should().Contain("Hauptstr. 1<br>10115 Berlin<br>Deutschland");
    }

    [Fact]
    public void Render_Empty_ShowsDash()
    {
        var field = Render<InlineAddress>(p => p
            .Add(a => a.Value, AddressData.Empty)
            .Add(a => a.Save, _ => Task.FromResult<Error?>(null)));

        field.Find(".inline-field-value").TextContent.Trim().Should().Be("–");
    }

    [Fact]
    public async Task Save_EditedFields_PassesAddressWithCountryCode()
    {
        AddressData? saved = null;
        var field = Render<InlineAddress>(p => p
            .Add(a => a.Value, new AddressData("Hauptstr. 1", null, "10115", "Berlin", null, "DE"))
            .Add(a => a.Save, a => { saved = a; return Task.FromResult<Error?>(null); }));

        await field.Find(".inline-field-display").ClickAsync(new());
        field.FindAll(".inline-address input")[3].Input("Potsdam");
        await field.FindAll("button").Single(b => b.TextContent.Contains("Speichern", StringComparison.Ordinal)).ClickAsync(new());

        saved.Should().Be(new AddressData("Hauptstr. 1", null, "10115", "Potsdam", null, "DE"));
    }

    [Fact]
    public async Task Save_ValidationError_StaysInEditModeAndShowsMessage()
    {
        var error = new ValidationError(new Dictionary<string, string[]> { ["Address.PostalCode"] = ["Die PLZ darf höchstens 20 Zeichen lang sein."] });
        var field = Render<InlineAddress>(p => p
            .Add(a => a.Value, AddressData.Empty)
            .Add(a => a.Save, _ => Task.FromResult<Error?>(error)));

        await field.Find(".inline-field-display").ClickAsync(new());
        await field.FindAll("button").Single(b => b.TextContent.Contains("Speichern", StringComparison.Ordinal)).ClickAsync(new());

        field.Find(".inline-field-error").TextContent.Should().Contain("PLZ");
        field.FindAll(".inline-address").Should().ContainSingle();
    }
}
