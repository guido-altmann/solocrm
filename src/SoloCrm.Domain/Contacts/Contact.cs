using SoloCrm.Domain.Common;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Domain.Contacts;

/// <summary>
/// A person the freelancer is in touch with (SPEC 2.3).
/// </summary>
public sealed class Contact : ArchivableEntity, IAuditable, IHasExtraFields
{
    public const int FirstNameMaxLength = 100;
    public const int LastNameMaxLength = 100;
    public const int EmailMaxLength = 320;
    public const int PhoneMaxLength = 50;
    public const int JobTitleMaxLength = 150;
    public const int LinkedInUrlMaxLength = 500;

    // Required by EF Core.
    private Contact()
    {
    }

    public string? FirstName { get; private set; }

    public string? LastName { get; private set; }

    /// <summary>Unique (case-insensitive) when set.</summary>
    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    public string? JobTitle { get; private set; }

    public string? LinkedInUrl { get; private set; }

    /// <summary>Optional employer; set to <c>null</c> by the database when the organization is deleted.</summary>
    public Guid? OrganizationId { get; private set; }

    public Organization? Organization { get; private set; }

    public LeadSource? Source { get; private set; }

    public IReadOnlyDictionary<string, string> ExtraFields { get; private set; } = new Dictionary<string, string>();

    /// <summary>Postal address (iteration 5 decision 14).</summary>
    public Address Address { get; private set; } = Address.Empty;

    /// <summary>
    /// Creates a contact. At least a first or a last name is required; blank values are stored as <c>null</c>.
    /// </summary>
    public static Contact Create(
        string? firstName,
        string? lastName,
        string? email = null,
        string? phone = null,
        string? jobTitle = null,
        string? linkedInUrl = null,
        Guid? organizationId = null,
        LeadSource? source = null,
        Address? address = null)
    {
        var contact = new Contact();
        contact.Update(firstName, lastName, email, phone, jobTitle, linkedInUrl, organizationId, source);
        contact.ChangeAddress(address ?? Address.Empty);
        contact.AddDomainEvent(new ContactCreated(contact.Id));
        return contact;
    }

    /// <summary>
    /// Replaces the master data; the same rules as for <see cref="Create"/> apply.
    /// </summary>
    public void Update(
        string? firstName,
        string? lastName,
        string? email,
        string? phone,
        string? jobTitle,
        string? linkedInUrl,
        Guid? organizationId,
        LeadSource? source)
    {
        var normalizedFirstName = Normalize(firstName);
        var normalizedLastName = Normalize(lastName);
        if (normalizedFirstName is null && normalizedLastName is null)
        {
            throw new ArgumentException("A contact requires a first or a last name.", nameof(lastName));
        }

        if (source is { } value && !Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown lead source.");
        }

        FirstName = normalizedFirstName;
        LastName = normalizedLastName;
        Email = Normalize(email);
        Phone = Normalize(phone);
        JobTitle = Normalize(jobTitle);
        LinkedInUrl = Normalize(linkedInUrl);
        OrganizationId = organizationId;
        Source = source;
    }

    public void ChangeAddress(Address address)
    {
        ArgumentNullException.ThrowIfNull(address);
        Address = address;
    }

    /// <summary>
    /// Sets or removes (blank value) an additional field, e.g. <c>HubSpotRecordId</c> from the CSV import (SPEC 2.2).
    /// A new dictionary is assigned, so the change tracker sees the jsonb value as modified.
    /// </summary>
    public void SetExtraField(string key, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var fields = new Dictionary<string, string>(ExtraFields, StringComparer.Ordinal);
        if (Normalize(value) is { } normalized)
        {
            fields[key] = normalized;
        }
        else
        {
            fields.Remove(key);
        }

        ExtraFields = fields;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
