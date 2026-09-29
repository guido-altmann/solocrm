namespace SoloCrm.Domain.Organizations;

/// <summary>
/// Validates and normalizes website URLs. The domain is the key for later enrichment (SPEC 9.5),
/// so equivalent inputs ("Example.com/", "https://example.com") must produce the same value.
/// </summary>
public static class Website
{
    /// <summary>
    /// Normalizes <paramref name="input"/>: adds <c>https://</c> when no scheme is given, lowercases scheme and host,
    /// drops the default port, the fragment and a trailing slash.
    /// </summary>
    /// <returns><c>false</c> if the input is not an http(s) URL with a domain name.</returns>
    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var value = input.Trim();
        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "https://" + value;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.HostNameType != UriHostNameType.Dns
            || !uri.Host.Contains('.', StringComparison.Ordinal)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        normalized = $"{uri.Scheme}://{uri.Authority}{path}{uri.Query}";
        return true;
    }
}
