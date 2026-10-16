namespace SoloCrm.Application.Features.Nextcloud;

/// <summary>Settings of the Nextcloud sync that come from the configuration (ADR-014).</summary>
public sealed class NextcloudSyncOptions
{
    /// <summary>
    /// <c>App:BaseUrl</c>: origin of SoloCRM for the links from tasks to their record. The sync runs in the background
    /// without a request, so there is no other source; without it the tasks in Nextcloud have no link.
    /// </summary>
    public string? AppBaseUrl { get; init; }
}
