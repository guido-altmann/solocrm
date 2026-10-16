namespace SoloCrm.Application.Features.Nextcloud;

/// <summary>
/// Which server URLs may be connected (ADR-014): <c>https</c> only, because the app password travels with every
/// request. <c>http</c> is allowed for the hosts in <c>Nextcloud:AllowedHttpHosts</c> (comma-separated), meant for
/// local test servers.
/// </summary>
public sealed class NextcloudServers(IEnumerable<string> allowedHttpHosts)
{
    public IReadOnlySet<string> AllowedHttpHosts { get; } =
        allowedHttpHosts.Select(h => h.Trim()).Where(h => h.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static NextcloudServers HttpsOnly { get; } = new([]);

    public bool IsAllowed(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
        && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment)
        && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && AllowedHttpHosts.Contains(uri.Host)));
}
