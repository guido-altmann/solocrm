using FolkerKinzel.VCards;
using FolkerKinzel.VCards.Enums;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Common;
using SoloCrm.Domain.Common;

namespace SoloCrm.Application.Features.Nextcloud;

/// <summary>
/// Renders a contact as vCard 3.0 for the address book „SoloCRM“ (ADR-014): <c>UID</c>, <c>N</c>/<c>FN</c>,
/// <c>EMAIL</c>, <c>TEL</c>, <c>ORG</c>, <c>TITLE</c>, <c>URL</c> (LinkedIn) and <c>ADR</c>. The output is
/// deterministic (no <c>REV</c>, no render time), because the sync detects changes by the hash of the payload.
/// </summary>
public static class ContactVCard
{
    public const string MediaType = "text/vcard; charset=utf-8";

    /// <param name="OrganizationName">Name of the employer, rendered as <c>ORG</c>.</param>
    public sealed record Card(
        Guid Id,
        string? FirstName,
        string? LastName,
        string? Email,
        string? Phone,
        string? JobTitle,
        string? LinkedInUrl,
        string? OrganizationName,
        AddressData Address);

    /// <summary><c>UID</c> of the record in Nextcloud; the suffix marks entries managed by SoloCRM.</summary>
    public static string Uid(Guid id) => $"{id}@solocrm";

    /// <summary>Name of the resource inside the address book.</summary>
    public static string ResourceName(Guid id) => $"{id}.vcf";

    public static DavContent Render(Card card)
    {
        ArgumentNullException.ThrowIfNull(card);

        var builder = VCardBuilder.Create(setContactID: false, setCreated: false)
            .ContactID.Set(Uid(card.Id))
            .NameViews.Add(NameBuilder.Create().AddSurname(card.LastName ?? "").AddGiven(card.FirstName ?? "").Build())
            .DisplayNames.Add(Names.Person(card.FirstName, card.LastName));

        if (card.Email is { } email)
        {
            builder.EMails.Add(email);
        }

        if (card.Phone is { } phone)
        {
            builder.Phones.Add(phone, p =>
            {
                p.PropertyClass = PCl.Work;
                p.PhoneType = Tel.Voice;
            });
        }

        if (!string.IsNullOrWhiteSpace(card.OrganizationName))
        {
            builder.Organizations.Add(card.OrganizationName);
        }

        if (card.JobTitle is { } title)
        {
            builder.Titles.Add(title);
        }

        if (card.LinkedInUrl is { } url)
        {
            builder.Urls.Add(url);
        }

        if (card.Address != AddressData.Empty)
        {
            var address = card.Address;
            var adr = AddressBuilder.Create();
            AddIfPresent(address.Street, v => adr.AddStreet(v));
            AddIfPresent(address.Street2, v => adr.AddExtended(v));
            AddIfPresent(address.PostalCode, v => adr.AddPostalCode(v));
            AddIfPresent(address.City, v => adr.AddLocality(v));
            AddIfPresent(address.Region, v => adr.AddRegion(v));
            AddIfPresent(Countries.ByIsoCode(address.CountryCode)?.GermanName, v => adr.AddCountry(v));
            builder.Addresses.Add(adr.Build(), p => p.PropertyClass = PCl.Work);
        }

        // UpdateTimeStamp would add REV with the render time and change the hash on every run.
        var body = Vcf.AsString([builder.VCard], VCdVersion.V3_0, options: VcfOpts.Default & ~VcfOpts.UpdateTimeStamp);
        return new DavContent(body, MediaType);
    }

    private static void AddIfPresent(string? value, Action<string> add)
    {
        if (!string.IsNullOrEmpty(value))
        {
            add(value);
        }
    }
}
