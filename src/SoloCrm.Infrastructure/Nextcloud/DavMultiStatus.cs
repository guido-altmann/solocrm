using System.Xml;
using System.Xml.Linq;

namespace SoloCrm.Infrastructure.Nextcloud;

/// <summary>XML namespaces and request bodies of WebDAV, CardDAV (RFC 6352) and CalDAV (RFC 4791).</summary>
internal static class Dav
{
    public static readonly XNamespace D = "DAV:";
    public static readonly XNamespace CardDav = "urn:ietf:params:xml:ns:carddav";
    public static readonly XNamespace CalDav = "urn:ietf:params:xml:ns:caldav";

    public static string PropFind(params XName[] properties) =>
        Serialize(new XElement(D + "propfind",
            new XAttribute(XNamespace.Xmlns + "d", D),
            new XAttribute(XNamespace.Xmlns + "card", CardDav),
            new XAttribute(XNamespace.Xmlns + "cal", CalDav),
            new XElement(D + "prop", properties.Select(p => new XElement(p)))));

    /// <summary>Extended MKCOL (RFC 5689) creating an address book with a display name.</summary>
    public static string MkAddressBook(string displayName) =>
        Serialize(new XElement(D + "mkcol",
            new XAttribute(XNamespace.Xmlns + "d", D),
            new XAttribute(XNamespace.Xmlns + "card", CardDav),
            new XElement(D + "set",
                new XElement(D + "prop",
                    new XElement(D + "resourcetype", new XElement(D + "collection"), new XElement(CardDav + "addressbook")),
                    new XElement(D + "displayname", displayName)))));

    /// <summary>MKCALENDAR (RFC 4791) creating a calendar for tasks only.</summary>
    public static string MkTaskList(string displayName) =>
        Serialize(new XElement(CalDav + "mkcalendar",
            new XAttribute(XNamespace.Xmlns + "d", D),
            new XAttribute(XNamespace.Xmlns + "cal", CalDav),
            new XElement(D + "set",
                new XElement(D + "prop",
                    new XElement(D + "displayname", displayName),
                    new XElement(CalDav + "supported-calendar-component-set", new XElement(CalDav + "comp", new XAttribute("name", "VTODO")))))));

    /// <summary><c>REPORT sync-collection</c> (RFC 6578) for the members' ETags; an empty token requests all members.</summary>
    public static string SyncCollection(string? syncToken) =>
        Serialize(new XElement(D + "sync-collection",
            new XAttribute(XNamespace.Xmlns + "d", D),
            new XElement(D + "sync-token", syncToken ?? ""),
            new XElement(D + "sync-level", "1"),
            new XElement(D + "prop", new XElement(D + "getetag"))));

    public static string Serialize(XElement root) =>
        new XDocument(new XDeclaration("1.0", "utf-8", null), root).Declaration + root.ToString(SaveOptions.DisableFormatting);
}

/// <summary>One <c>response</c> of a <c>207 Multi-Status</c>: the href and the properties reported with status 200.</summary>
internal sealed record DavResponse(string Href, IReadOnlyDictionary<XName, XElement> Properties, int? Status)
{
    public XElement? this[XName name] => Properties.GetValueOrDefault(name);

    public string? Text(XName name) => this[name]?.Value is { Length: > 0 } value ? value : null;

    /// <summary>The first <c>href</c> inside a property such as <c>current-user-principal</c>.</summary>
    public string? HrefIn(XName name) => this[name]?.Element(Dav.D + "href")?.Value.Trim() is { Length: > 0 } href ? href : null;

    public bool HasResourceType(XName type) => this[Dav.D + "resourcetype"]?.Element(type) is not null;
}

/// <summary>Parses <c>207 Multi-Status</c> bodies; hrefs are reduced to absolute paths.</summary>
internal static class DavMultiStatus
{
    /// <exception cref="FormatException">The body is no multistatus document.</exception>
    public static IReadOnlyList<DavResponse> Parse(string body) => ParseWithSyncToken(body).Responses;

    /// <summary>Like <see cref="Parse"/>, plus the <c>sync-token</c> of a <c>sync-collection</c> report.</summary>
    /// <exception cref="FormatException">The body is no multistatus document.</exception>
    public static (IReadOnlyList<DavResponse> Responses, string? SyncToken) ParseWithSyncToken(string body)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(body);
        }
        catch (XmlException ex)
        {
            throw new FormatException("The body is no XML document.", ex);
        }

        if (document.Root?.Name != Dav.D + "multistatus")
        {
            throw new FormatException("The body is no multistatus document.");
        }

        var responses = document.Root.Elements(Dav.D + "response")
            .Select(response =>
            {
                var href = ToPath(response.Element(Dav.D + "href")?.Value);
                var properties = response.Elements(Dav.D + "propstat")
                    .Where(ps => StatusCode(ps.Element(Dav.D + "status")?.Value) is >= 200 and < 300)
                    .SelectMany(ps => ps.Element(Dav.D + "prop")?.Elements() ?? [])
                    .GroupBy(p => p.Name)
                    .ToDictionary(g => g.Key, g => g.First());
                return new DavResponse(href, properties, StatusCode(response.Element(Dav.D + "status")?.Value));
            })
            .Where(r => r.Href.Length > 0)
            .ToList();
        var syncToken = document.Root.Element(Dav.D + "sync-token")?.Value.Trim();
        return (responses, string.IsNullOrEmpty(syncToken) ? null : syncToken);
    }

    /// <summary>Servers may answer with full URLs or paths; SoloCRM stores paths.</summary>
    public static string ToPath(string? href)
    {
        var trimmed = href?.Trim() ?? "";
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri.AbsolutePath : trimmed;
    }

    /// <summary>„HTTP/1.1 404 Not Found“ → 404.</summary>
    private static int? StatusCode(string? statusLine)
    {
        var parts = statusLine?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts is { Length: >= 2 } && int.TryParse(parts[1], out var code) ? code : null;
    }
}
