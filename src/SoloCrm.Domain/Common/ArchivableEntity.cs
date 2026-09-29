namespace SoloCrm.Domain.Common;

/// <summary>
/// Entity that is hidden by archiving instead of being deleted (SPEC 2.2).
/// The audit interceptor records archiving as <c>Archived</c> instead of <c>Updated</c>.
/// </summary>
public abstract class ArchivableEntity : Entity
{
    public bool IsArchived { get; private set; }

    public void Archive() => IsArchived = true;

    public void Restore() => IsArchived = false;
}
