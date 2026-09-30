using Bunit;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor.Services;
using SoloCrm.Application.Abstractions;
using SoloCrm.Web.Components.Shared;

namespace SoloCrm.Web.Tests.Components.Shared;

/// <summary>Inline editing (US-14): Enter/blur saves, Esc cancels, errors stay at the field.</summary>
public sealed class InlineFieldTests : BunitContext
{
    private readonly List<string?> _saved = [];
    private Error? _nextError;

    public InlineFieldTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    [Theory]
    [InlineData("Enter")]
    [InlineData("F2")]
    public async Task KeyOnDisplay_StartsEditing(string key)
    {
        var field = RenderText("+49 30 1");

        await field.Find(".inline-field-display").KeyDownAsync(new KeyboardEventArgs { Key = key });

        field.Find("input").GetAttribute("value").Should().Be("+49 30 1");
    }

    [Fact]
    public async Task Enter_ChangedValue_SavesAndLeavesEditMode()
    {
        var field = RenderText("alt");
        await field.Find(".inline-field-display").ClickAsync(new MouseEventArgs());

        await field.Find("input").InputAsync(new() { Value = "neu" });
        await field.Find(".inline-field-editor").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        _saved.Should().Equal("neu");
        field.FindAll("input").Should().BeEmpty();
    }

    [Fact]
    public async Task Blur_ChangedValue_Saves()
    {
        var field = RenderText("alt");
        await field.Find(".inline-field-display").ClickAsync(new MouseEventArgs());

        await field.Find("input").InputAsync(new() { Value = "neu" });
        await field.Find("input").BlurAsync(new FocusEventArgs());

        _saved.Should().Equal("neu");
    }

    [Fact]
    public async Task Escape_DiscardsChangeWithoutSaving()
    {
        var field = RenderText("alt");
        await field.Find(".inline-field-display").ClickAsync(new MouseEventArgs());

        await field.Find("input").InputAsync(new() { Value = "neu" });
        await field.Find(".inline-field-editor").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        _saved.Should().BeEmpty();
        field.Find(".inline-field-value").TextContent.Trim().Should().Be("alt");
    }

    [Fact]
    public async Task Enter_UnchangedValue_LeavesEditModeWithoutSaving()
    {
        var field = RenderText("alt");
        await field.Find(".inline-field-display").ClickAsync(new MouseEventArgs());

        await field.Find(".inline-field-editor").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        _saved.Should().BeEmpty();
        field.FindAll("input").Should().BeEmpty();
    }

    [Fact]
    public async Task Enter_ValidationError_ShowsMessageAtFieldAndStaysInEditMode()
    {
        _nextError = new ValidationError(new Dictionary<string, string[]>
        {
            ["Email"] = ["Bitte eine gültige E-Mail-Adresse angeben."],
            ["Other"] = ["Gehört zu einem anderen Feld."],
        });
        var field = RenderText("ada@example.test");
        await field.Find(".inline-field-display").ClickAsync(new MouseEventArgs());

        await field.Find("input").InputAsync(new() { Value = "keine mail" });
        await field.Find(".inline-field-editor").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        field.Find(".inline-field-error").TextContent.Should().Be("Bitte eine gültige E-Mail-Adresse angeben.");
        field.Find("input").GetAttribute("value").Should().Be("keine mail");
    }

    [Fact]
    public async Task Enter_BusinessError_ShowsItsMessage()
    {
        _nextError = Error.Conflict("Contact.DuplicateEmail", "Ein Kontakt mit dieser E-Mail-Adresse existiert bereits.");
        var field = RenderText("ada@example.test");
        await field.Find(".inline-field-display").ClickAsync(new MouseEventArgs());

        await field.Find("input").InputAsync(new() { Value = "grace@example.test" });
        await field.Find(".inline-field-editor").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        field.Find(".inline-field-error").TextContent.Should().Contain("existiert bereits");
    }

    [Fact]
    public async Task Multiline_EnterAddsLineAndCtrlEnterSaves()
    {
        var field = RenderText("Zeile", multiline: true);
        await field.Find(".inline-field-display").ClickAsync(new MouseEventArgs());
        await field.Find("textarea").InputAsync(new() { Value = "Zeile\nzwei" });

        await field.Find(".inline-field-editor").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        _saved.Should().BeEmpty();
        await field.Find(".inline-field-editor").KeyDownAsync(new KeyboardEventArgs { Key = "Enter", CtrlKey = true });

        _saved.Should().Equal("Zeile\nzwei");
    }

    private IRenderedComponent<InlineText> RenderText(string? value, bool multiline = false) =>
        Render<InlineText>(p => p
            .Add(t => t.Label, "E-Mail")
            .Add(t => t.Field, "Email")
            .Add(t => t.Value, value)
            .Add(t => t.Multiline, multiline)
            .Add(t => t.Save, v =>
            {
                if (_nextError is { } error)
                {
                    return Task.FromResult<Error?>(error);
                }

                _saved.Add(v);
                return Task.FromResult<Error?>(null);
            }));
}
