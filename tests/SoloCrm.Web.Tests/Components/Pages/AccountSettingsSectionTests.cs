using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using SoloCrm.Web.Components.Pages.Settings;

namespace SoloCrm.Web.Tests.Components.Pages;

public sealed class AccountSettingsSectionTests : BunitContext
{
    public AccountSettingsSectionTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    [Theory]
    [InlineData("account-change-password", "Account/Manage/ChangePassword")]
    [InlineData("account-2fa", "Account/Manage/TwoFactorAuthentication")]
    [InlineData("account-passkeys", "Account/Manage/Passkeys")]
    public void Click_AccountLink_LoadsStaticAccountPage(string button, string path)
    {
        var navigation = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
        var section = Render<AccountSettingsSection>();

        section.Find($"[data-testid={button}]").Click();

        navigation.History.Should().ContainSingle()
            .Which.Should().Match<NavigationHistory>(h => h.Uri == path && h.Options.ForceLoad);
    }
}
