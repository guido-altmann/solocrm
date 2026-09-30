namespace SoloCrm.Web.Components.Shared;

/// <summary>Suggestion of <see cref="TagInput"/>: an existing tag, or (without <see cref="Id"/>) one to create inline.</summary>
public sealed record TagOption(Guid? Id, string Name, string? Color)
{
    public bool IsNew => Id is null;
}
