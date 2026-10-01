using FluentValidation;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Webhooks;

namespace SoloCrm.Application.Features.Webhooks;

public static class WebhookErrors
{
    public static Error NotFound { get; } =
        Error.NotFound("Webhook.NotFound", "Der Webhook wurde nicht gefunden.");
}

internal static class WebhookValidation
{
    public static IRuleBuilderOptions<T, string?> ValidWebhookName<T>(this IRuleBuilder<T, string?> rule) =>
        rule
            .NotEmpty()
            .WithMessage("Bitte einen Namen angeben.")
            .Must(name => name is null || name.Trim().Length <= WebhookSubscription.NameMaxLength)
            .WithMessage($"Der Name darf höchstens {WebhookSubscription.NameMaxLength} Zeichen lang sein.");

    public static IRuleBuilderOptions<T, string?> ValidWebhookUrl<T>(this IRuleBuilder<T, string?> rule, WebhookTargets targets) =>
        rule
            .NotEmpty()
            .WithMessage("Bitte eine URL angeben.")
            .Must(url => url is null || url.Trim().Length <= WebhookSubscription.UrlMaxLength)
            .WithMessage($"Die URL darf höchstens {WebhookSubscription.UrlMaxLength} Zeichen lang sein.")
            .Must(url => string.IsNullOrWhiteSpace(url) || targets.IsAllowed(url))
            .WithMessage(targets.AllowedHttpHosts.Count == 0
                ? "Bitte eine gültige https-URL angeben."
                : $"Bitte eine gültige https-URL angeben (http nur für {string.Join(", ", targets.AllowedHttpHosts.Order(StringComparer.Ordinal))}).");

    public static IRuleBuilderOptions<T, IReadOnlyList<string>?> ValidWebhookEvents<T>(this IRuleBuilder<T, IReadOnlyList<string>?> rule) =>
        rule
            .Must(events => events is { Count: > 0 })
            .WithMessage("Bitte mindestens ein Ereignis auswählen.")
            .Must(events => events is null || events.All(WebhookEvents.IsKnown))
            .WithMessage("Unbekanntes Ereignis.");
}
