using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using SoloCrm.Web.Components.Layout;

namespace SoloCrm.Web.Tests.Components.Layout;

public sealed class UserMenuTests : BunitContext
{
    private IRenderedComponent<MudBlazor.MudPopoverProvider> _popovers = null!;

    public UserMenuTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
        AddAuthorization().SetAuthorized("ada");
    }

    [Fact]
    public async Task Account_Clicked_ClosesMenuAndNavigatesWithForceLoad()
    {
        _popovers = Render<MudBlazor.MudPopoverProvider>();
        var menu = Render<UserMenu>();
        await menu.Find("button").ClickAsync(new());

        await _popovers.WaitForElement("[data-testid=user-menu-account]").ClickAsync(new());

        var navigation = Services.GetRequiredService<NavigationManager>().Should().BeOfType<BunitNavigationManager>().Subject;
        var entry = navigation.History.Should().ContainSingle(h => h.Uri == "Account/Manage").Subject;
        entry.Options.ForceLoad.Should().BeTrue();
        _popovers.WaitForAssertion(() => _popovers.FindAll("[data-testid=user-menu-account]").Should().BeEmpty());
    }
}
