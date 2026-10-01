using SoloCrm.Domain.Activities;
using SoloCrm.Domain.Common;
using SoloCrm.Domain.Contacts;
using SoloCrm.Domain.Opportunities;
using SoloCrm.Domain.Organizations;
using SoloCrm.Domain.Tasks;

namespace SoloCrm.Domain.Webhooks;

/// <summary>
/// The events a webhook subscription can select (SPEC 2.6). A new domain event only needs an entry here.
/// </summary>
public static class WebhookEvents
{
    /// <summary>Sent by „Test senden“ in the settings; not selectable and never written to the outbox.</summary>
    public const string Ping = "webhook.ping";

    public static IReadOnlyList<string> All { get; } =
    [
        DomainEventNames.Of(typeof(ContactCreated)),
        DomainEventNames.Of(typeof(OrganizationCreated)),
        DomainEventNames.Of(typeof(OpportunityCreated)),
        DomainEventNames.Of(typeof(OpportunityStageChanged)),
        DomainEventNames.Of(typeof(TaskCompleted)),
        DomainEventNames.Of(typeof(TaskReopened)),
        DomainEventNames.Of(typeof(ActivityLogged)),
    ];

    public static bool IsKnown(string eventType) => All.Contains(eventType, StringComparer.Ordinal);
}
