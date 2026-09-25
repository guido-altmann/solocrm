using MudBlazor;

namespace SoloCrm.Web.Components.Layout;

internal static class SoloCrmTheme
{
    public static MudTheme Instance { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#1E5AA8",
            Secondary = "#00897B",
            AppbarBackground = "#1E5AA8",
            Background = "#F6F7F9",
            DrawerBackground = "#FFFFFF",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#6EA8FE",
            Secondary = "#4DB6AC",
            AppbarBackground = "#1B1E24",
            Background = "#121418",
            Surface = "#1B1E24",
            DrawerBackground = "#1B1E24",
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "6px",
        },
        // System font stack: no external font CDN (privacy, see SPEC 6)
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = ["system-ui", "-apple-system", "Segoe UI", "Roboto", "Helvetica Neue", "Arial", "sans-serif"],
            },
        },
    };
}
