namespace SoloCrm.Infrastructure.Persistence.Outbox;

/// <summary>Configuration section <c>Outbox</c> (ADR-008, SPEC 7.4).</summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>Switches the background processing off, e.g. in tests that inspect the outbox.</summary>
    public bool Enabled { get; set; } = true;

    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(10);

    public int BatchSize { get; set; } = 20;

    /// <summary>
    /// How long a claimed message stays invisible to other processors. Must exceed the time to deliver a batch;
    /// after a crash the message is picked up again once the lease expires (at-least-once).
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Processed messages and the delivery log are deleted after this period (iteration 5 decision 8).</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(30);

    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromDays(1);
}
