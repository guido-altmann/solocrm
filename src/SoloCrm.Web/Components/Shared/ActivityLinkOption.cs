namespace SoloCrm.Web.Components.Shared;

/// <summary>An additional record an activity can optionally be linked to, e.g. „Auch bei Max Mustermann“.</summary>
public sealed record ActivityLinkOption(string Label, Guid? ContactId = null, Guid? OrganizationId = null);
