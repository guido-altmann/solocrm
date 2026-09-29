namespace SoloCrm.Application.Abstractions;

/// <summary>Id and display name of a related contact, organization or opportunity (for links in the UI).</summary>
public sealed record RecordRef(Guid Id, string Name);

public static class Names
{
    /// <summary>„Max Mustermann“; one of the names may be missing (SPEC 2.3).</summary>
    public static string Person(string? firstName, string? lastName) =>
        string.Join(" ", new[] { firstName, lastName }.Where(n => !string.IsNullOrWhiteSpace(n)));
}
