using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using SoloCrm.Web.Components.Shared;

namespace SoloCrm.Web.Tests.Components.Shared;

public sealed class QuickAddServiceTests : BunitContext
{
    private readonly QuickAddService _quickAdd;
    private readonly NavigationManager _navigation;

    public QuickAddServiceTests()
    {
        Services.AddMudServices();
        _navigation = Services.GetRequiredService<NavigationManager>();
        _quickAdd = new QuickAddService(Services.GetRequiredService<IDialogService>(), Services.GetRequiredService<ISnackbar>(), _navigation);
    }

    [Theory]
    [InlineData("/", QuickAddTarget.Contact)]
    [InlineData("/contacts", QuickAddTarget.Contact)]
    [InlineData("/organizations", QuickAddTarget.Organization)]
    [InlineData("/pipeline", QuickAddTarget.Opportunity)]
    public void CurrentTarget_ListPages_DependsOnPath(string path, QuickAddTarget expected)
    {
        _navigation.NavigateTo(path);

        _quickAdd.CurrentTarget.Should().Be(expected);
    }

    [Fact]
    public async Task OpenForCurrentPage_DetailViewWithNoteInput_FocusesNote()
    {
        var focused = 0;
        _navigation.NavigateTo($"/organizations/{Guid.CreateVersion7()}");
        using var registration = _quickAdd.RegisterNoteInput(() =>
        {
            focused++;
            return Task.CompletedTask;
        });

        await _quickAdd.OpenForCurrentPageAsync();

        _quickAdd.CurrentTarget.Should().Be(QuickAddTarget.Note);
        _quickAdd.CurrentLabel.Should().Be("Neue Notiz");
        focused.Should().Be(1);
    }

    [Fact]
    public void CurrentTarget_RegistrationDisposedOrListPage_FallsBack()
    {
        var registration = _quickAdd.RegisterNoteInput(() => Task.CompletedTask);
        _navigation.NavigateTo("/organizations");
        _quickAdd.CurrentTarget.Should().Be(QuickAddTarget.Organization, "a list page never adds notes");

        _navigation.NavigateTo($"/contacts/{Guid.CreateVersion7()}");
        registration.Dispose();

        _quickAdd.CurrentTarget.Should().Be(QuickAddTarget.Contact);
    }

    [Fact]
    public void Dispose_OlderRegistration_KeepsNewerOne()
    {
        _navigation.NavigateTo($"/contacts/{Guid.CreateVersion7()}");
        var older = _quickAdd.RegisterNoteInput(() => Task.CompletedTask);
        using var newer = _quickAdd.RegisterNoteInput(() => Task.CompletedTask);

        older.Dispose();

        _quickAdd.CurrentTarget.Should().Be(QuickAddTarget.Note);
    }
}
