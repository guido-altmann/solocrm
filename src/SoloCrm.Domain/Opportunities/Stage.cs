using SoloCrm.Domain.Common;

namespace SoloCrm.Domain.Opportunities;

/// <summary>
/// Phase of the pipeline (SPEC 2.3). At least one stage each with <see cref="StageStatus.Won"/>
/// and <see cref="StageStatus.Lost"/> must exist; the use cases enforce this.
/// </summary>
public sealed class Stage : Entity, IAuditable
{
    public const int NameMaxLength = 100;

    // Required by EF Core.
    private Stage()
    {
        Name = null!;
    }

    private Stage(string name, int sortOrder, StageStatus status)
    {
        Name = name;
        SortOrder = sortOrder;
        Status = status;
    }

    public string Name { get; private set; }

    public int SortOrder { get; private set; }

    public StageStatus Status { get; private set; }

    public static Stage Create(string name, int sortOrder, StageStatus status)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown stage status.");
        }

        return new Stage(RequireName(name), sortOrder, status);
    }

    public void Rename(string name) => Name = RequireName(name);

    public void MoveTo(int sortOrder) => SortOrder = sortOrder;

    private static string RequireName(string name) =>
        string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("A stage requires a name.", nameof(name))
            : name.Trim();
}
