namespace SoloCrm.Application.Features.Webhooks;

/// <summary>
/// Which webhook URLs are allowed (iteration 5 decision 7): <c>https</c> always, <c>http</c> only for the hosts in
/// <c>Webhooks:AllowedHttpHosts</c> (comma-separated). Private addresses are allowed (n8n often runs internally).
/// </summary>
public sealed class WebhookTargets(IEnumerable<string> allowedHttpHosts)
{
    public IReadOnlySet<string> AllowedHttpHosts { get; } =
        allowedHttpHosts.Select(h => h.Trim()).Where(h => h.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static WebhookTargets HttpsOnly { get; } = new([]);

    public bool IsAllowed(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
        && string.IsNullOrEmpty(uri.UserInfo)
        && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && AllowedHttpHosts.Contains(uri.Host)));
}
