using SoloCrm.Domain.Contacts;
using SoloCrm.Domain.Opportunities;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Domain.Tags;

/// <summary>
/// Assignment of a <see cref="Tag"/> to a record, stored in one typed join table per record type (ADR-005).
/// The audit interceptor records adding and removing as a change of the record's field <c>Tags</c>
/// (iteration 4 decision 3); the timeline does not show it.
/// </summary>
public interface ITagAssignment
{
    /// <summary>Id of the tagged contact, organization or request.</summary>
    Guid RecordId { get; }

    /// <summary>CLR type name of the tagged record, as used by <c>AuditEntry.EntityType</c>.</summary>
    string RecordType { get; }

    Guid TagId { get; }

    Tag? Tag { get; }
}

public sealed class ContactTag : ITagAssignment
{
    // Required by EF Core.
    private ContactTag()
    {
    }

    public ContactTag(Guid contactId, Tag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        ContactId = contactId;
        TagId = tag.Id;
        Tag = tag;
    }

    public Guid ContactId { get; private set; }

    public Guid TagId { get; private set; }

    public Tag? Tag { get; private set; }

    Guid ITagAssignment.RecordId => ContactId;

    string ITagAssignment.RecordType => nameof(Contact);
}

public sealed class OrganizationTag : ITagAssignment
{
    // Required by EF Core.
    private OrganizationTag()
    {
    }

    public OrganizationTag(Guid organizationId, Tag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        OrganizationId = organizationId;
        TagId = tag.Id;
        Tag = tag;
    }

    public Guid OrganizationId { get; private set; }

    public Guid TagId { get; private set; }

    public Tag? Tag { get; private set; }

    Guid ITagAssignment.RecordId => OrganizationId;

    string ITagAssignment.RecordType => nameof(Organization);
}

public sealed class OpportunityTag : ITagAssignment
{
    // Required by EF Core.
    private OpportunityTag()
    {
    }

    public OpportunityTag(Guid opportunityId, Tag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        OpportunityId = opportunityId;
        TagId = tag.Id;
        Tag = tag;
    }

    public Guid OpportunityId { get; private set; }

    public Guid TagId { get; private set; }

    public Tag? Tag { get; private set; }

    Guid ITagAssignment.RecordId => OpportunityId;

    string ITagAssignment.RecordType => nameof(Opportunity);
}
