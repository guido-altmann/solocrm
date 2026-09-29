using SoloCrm.Domain.Opportunities;

namespace SoloCrm.Infrastructure.Persistence.Seeding;

/// <summary>
/// The six standard stages (SPEC 2.3), seeded by migration with fixed ids.
/// </summary>
public static class DefaultStages
{
    public static readonly Guid New = new("0199a000-0000-7000-8000-000000000001");
    public static readonly Guid Applied = new("0199a000-0000-7000-8000-000000000002");
    public static readonly Guid InTalks = new("0199a000-0000-7000-8000-000000000003");
    public static readonly Guid Offer = new("0199a000-0000-7000-8000-000000000004");
    public static readonly Guid Won = new("0199a000-0000-7000-8000-000000000005");
    public static readonly Guid Lost = new("0199a000-0000-7000-8000-000000000006");

    internal static readonly DateTimeOffset SeededAt = new(2026, 9, 29, 0, 0, 0, TimeSpan.Zero);

    internal static IEnumerable<object> SeedData =>
    [
        Row(New, "Neu", 1, StageStatus.Open),
        Row(Applied, "Beworben", 2, StageStatus.Open),
        Row(InTalks, "Im Gespräch", 3, StageStatus.Open),
        Row(Offer, "Angebot", 4, StageStatus.Open),
        Row(Won, "Gewonnen", 5, StageStatus.Won),
        Row(Lost, "Verloren", 6, StageStatus.Lost),
    ];

    private static object Row(Guid id, string name, int sortOrder, StageStatus status) => new
    {
        Id = id,
        Name = name,
        SortOrder = sortOrder,
        Status = status,
        CreatedAt = SeededAt,
        UpdatedAt = SeededAt,
    };
}
