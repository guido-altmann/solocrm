namespace SoloCrm.Infrastructure.Mail;

/// <summary>Configuration section <c>Mail:Graph</c>: the Entra app registration of the user (ADR-013).</summary>
public sealed class GraphMailOptions
{
    public const string SectionName = "Mail:Graph";

    public string? TenantId { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(TenantId) && !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>Configuration section <c>Mail</c>: the background sync.</summary>
public sealed class MailWorkerOptions
{
    public const string SectionName = "Mail";

    public static readonly TimeSpan MinimumPollingInterval = TimeSpan.FromSeconds(30);

    /// <summary>Switches the background sync off, e.g. in tests.</summary>
    public bool Enabled { get; set; } = true;

    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromMinutes(2);
}
