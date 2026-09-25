using SoloCrm.Domain.Common;

namespace SoloCrm.Domain.Contacts;

/// <summary>
/// A person the freelancer is in touch with (SPEC 2.3).
/// </summary>
public sealed class Contact : Entity
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

    private Contact(string? firstName, string? lastName)
    {
        FirstName = firstName;
        LastName = lastName;
    }

    public string? FirstName { get; private set; }

    public string? LastName { get; private set; }

    /// <summary>Unique (case-insensitive) when set.</summary>
    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    public string? JobTitle { get; private set; }

    public string? LinkedInUrl { get; private set; }

    /// <summary>
    /// Creates a contact. At least a first or a last name is required; blank values are stored as <c>null</c>.
    /// </summary>
    public static Contact Create(
        string? firstName,
        string? lastName,
        string? email = null,
        string? phone = null,
        string? jobTitle = null,
        string? linkedInUrl = null)
    {
        var contact = new Contact(Normalize(firstName), Normalize(lastName))
        {
            Email = Normalize(email),
            Phone = Normalize(phone),
            JobTitle = Normalize(jobTitle),
            LinkedInUrl = Normalize(linkedInUrl),
        };

        if (contact.FirstName is null && contact.LastName is null)
        {
            throw new ArgumentException("A contact requires a first or a last name.", nameof(lastName));
        }

        contact.AddDomainEvent(new ContactCreated(contact.Id));
        return contact;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
