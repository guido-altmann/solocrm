namespace SoloCrm.Web.Components.Shared;

/// <summary>Key that saves an <see cref="InlineField"/> in edit mode.</summary>
public enum InlineSaveKey
{
    Enter,

    /// <summary>Multiline text, where Enter starts a new line.</summary>
    CtrlEnter,

    /// <summary>The editor saves itself, e.g. on selection; Enter belongs to the editor.</summary>
    None,
}
