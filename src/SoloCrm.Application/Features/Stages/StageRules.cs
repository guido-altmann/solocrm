using FluentValidation;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Opportunities;

namespace SoloCrm.Application.Features.Stages;

public static class StageErrors
{
    public static Error NotFound { get; } =
        Error.NotFound("Stage.NotFound", "Die Phase wurde nicht gefunden.");

    public static Error LastOfStatus(StageStatus status) => Error.Conflict(
        "Stage.LastOfStatus",
        status switch
        {
            StageStatus.Won => "Es muss mindestens eine Phase „Gewonnen“ geben.",
            StageStatus.Lost => "Es muss mindestens eine Phase „Verloren“ geben.",
            _ => "Es muss mindestens eine offene Phase geben.",
        });

    public static Error TargetRequired { get; } =
        Error.Conflict("Stage.TargetRequired", "Der Phase sind Anfragen zugeordnet. Bitte eine Ziel-Phase für die Umverteilung wählen.");

    public static Error InvalidTarget { get; } =
        Error.Conflict("Stage.InvalidTarget", "Die Ziel-Phase muss eine andere Phase mit demselben Status sein.");

    public static Error IncompleteOrder { get; } =
        Error.Conflict("Stage.IncompleteOrder", "Die neue Reihenfolge muss alle Phasen genau einmal enthalten.");
}

internal static class StageValidation
{
    public static IRuleBuilderOptions<T, string?> ValidStageName<T>(this IRuleBuilder<T, string?> rule) =>
        rule
            .NotEmpty()
            .WithMessage("Bitte einen Namen angeben.")
            .MaximumLength(Stage.NameMaxLength)
            .WithMessage($"Der Name darf höchstens {Stage.NameMaxLength} Zeichen lang sein.");
}
