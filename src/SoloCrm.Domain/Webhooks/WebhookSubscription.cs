using SoloCrm.Domain.Common;

namespace SoloCrm.Domain.Webhooks;

/// <summary>
/// A target URL that receives the selected events as signed webhooks (SPEC 2.4, US-18). Not audited: the
/// protected secret must not end up in audit entries.
/// </summary>
public sealed class WebhookSubscription : Entity
{
    public const int NameMaxLength = 100;
    public const int UrlMaxLength = 2000;

    private List<string> _events;

    // Required by EF Core.
    private WebhookSubscription()
    {
        Name = null!;
        Url = null!;
        ProtectedSecret = null!;
        _events = [];
    }

    private WebhookSubscription(string name, string url, IEnumerable<string> events, string protectedSecret)
    {
        Name = RequireName(name);
        Url = RequireUrl(url);
        _events = RequireEvents(events);
        ProtectedSecret = RequireSecret(protectedSecret);
        IsActive = true;
    }

    public string Name { get; private set; }

    /// <summary>Absolute <c>https</c> URL; <c>http</c> only for configured hosts, which the use case checks.</summary>
    public string Url { get; private set; }

    /// <summary>Public event names (<see cref="WebhookEvents.All"/>), sorted and distinct.</summary>
    public IReadOnlyList<string> Events => _events;

    /// <summary>The signing secret, encrypted with Data Protection (ADR-010); never shown again after creation.</summary>
    public string ProtectedSecret { get; private set; }

    public bool IsActive { get; private set; }

    public static WebhookSubscription Create(string name, string url, IEnumerable<string> events, string protectedSecret) =>
        new(name, url, events, protectedSecret);

    public void Update(string name, string url, IEnumerable<string> events, bool isActive)
    {
        Name = RequireName(name);
        Url = RequireUrl(url);
        _events = RequireEvents(events);
        IsActive = isActive;
    }

    public void ReplaceSecret(string protectedSecret) => ProtectedSecret = RequireSecret(protectedSecret);

    /// <summary>
    /// A subscription receives an event only when it is active, has selected the event type and existed
    /// when the event occurred (iteration 5 decision 3).
    /// </summary>
    public bool Receives(string eventType, DateTimeOffset occurredAt) =>
        IsActive && occurredAt >= CreatedAt && _events.Contains(eventType, StringComparer.Ordinal);

    private static string RequireName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var trimmed = name.Trim();
        return trimmed.Length <= NameMaxLength
            ? trimmed
            : throw new ArgumentException($"A webhook name has at most {NameMaxLength} characters.", nameof(name));
    }

    private static string RequireUrl(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        var trimmed = url.Trim();
        return trimmed.Length <= UrlMaxLength
            && Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                ? trimmed
                : throw new ArgumentException("A webhook requires an absolute http(s) URL.", nameof(url));
    }

    private static List<string> RequireEvents(IEnumerable<string> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var list = events.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (list.Count == 0)
        {
            throw new ArgumentException("A webhook requires at least one event.", nameof(events));
        }

        var unknown = list.FirstOrDefault(e => !WebhookEvents.IsKnown(e));
        return unknown is null ? list : throw new ArgumentException($"'{unknown}' is not a webhook event.", nameof(events));
    }

    private static string RequireSecret(string protectedSecret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedSecret);
        return protectedSecret;
    }
}
