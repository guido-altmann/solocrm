namespace SoloCrm.Application.Features.Common;

/// <summary>Absolute links into the app for records that leave it, e.g. tasks in calendars and Nextcloud.</summary>
public static class RecordLinks
{
    /// <summary>
    /// Link to the most specific record a task is linked to: opportunity, then contact, then organization.
    /// <c>null</c> for free tasks or without <paramref name="baseUrl"/>.
    /// </summary>
    /// <param name="baseUrl">Origin of the app, with or without trailing slash.</param>
    public static Uri? ForTask(string? baseUrl, Guid? opportunityId, Guid? contactId, Guid? organizationId)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        var path = (opportunityId, contactId, organizationId) switch
        {
            ({ } id, _, _) => $"/opportunities/{id}",
            (_, { } id, _) => $"/contacts/{id}",
            (_, _, { } id) => $"/organizations/{id}",
            _ => null,
        };

        return path is not null && Uri.TryCreate(baseUrl.Trim().TrimEnd('/') + path, UriKind.Absolute, out var uri) ? uri : null;
    }
}
