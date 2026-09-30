using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using SoloCrm.Web.Components.Shared;

namespace SoloCrm.Web.Tests.Components.Shared;

/// <summary>Shortcut targets and the „?“ overview (SPEC 3.2). The key handling itself lives in keyboard.js.</summary>
public sealed class KeyboardShortcutsTests : BunitContext
{
    public KeyboardShortcutsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    [Theory]
    [InlineData("g h", "/")]
    [InlineData("g p", "/pipeline")]
    [InlineData("g k", "/contacts")]
    [InlineData("g o", "/organizations")]
    [InlineData("g x", null)]
    [InlineData("n", null)]
    public void NavigationTarget_Sequence_ReturnsRoute(string shortcut, string? expected) =>
        KeyboardShortcuts.NavigationTarget(shortcut).Should().Be(expected);

    [Theory]
    [InlineData(false, "Strg")]
    [InlineData(true, "⌘")]
    public async Task Dialog_Platform_ListsAllShortcutsWithModifier(bool isMac, string modifier)
    {
        var provider = Render<MudDialogProvider>();

        await provider.InvokeAsync(() => Services.GetRequiredService<IDialogService>().ShowAsync<KeyboardShortcutsDialog>(
            "Tastenkürzel", new DialogParameters<KeyboardShortcutsDialog> { { d => d.IsMac, isMac } }));

        var rows = provider.FindAll("tr").Select(r => r.TextContent.Trim()).ToList();
        rows.Should().Contain(r => r.StartsWith($"{modifier} + K", StringComparison.Ordinal));
        rows.Should().Contain(r => r.StartsWith("G dann P", StringComparison.Ordinal) && r.EndsWith("Gehe zu Pipeline", StringComparison.Ordinal));
        rows.Should().HaveCount(KeyboardShortcuts.Overview(isMac).Sum(g => g.Shortcuts.Count));
    }
}
