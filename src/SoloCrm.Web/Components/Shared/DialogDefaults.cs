using MudBlazor;

namespace SoloCrm.Web.Components.Shared;

/// <summary>Shared dialog sizes; <c>Esc</c> closes every dialog (SPEC 3.2).</summary>
public static class DialogDefaults
{
    public static DialogOptions Small { get; } = new()
    {
        MaxWidth = MaxWidth.Small,
        FullWidth = true,
        CloseOnEscapeKey = true,
    };

    public static DialogOptions Medium { get; } = new()
    {
        MaxWidth = MaxWidth.Medium,
        FullWidth = true,
        CloseOnEscapeKey = true,
    };
}
