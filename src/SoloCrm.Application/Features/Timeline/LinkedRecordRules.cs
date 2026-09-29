using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Common;

namespace SoloCrm.Application.Features.Timeline;

/// <summary>Errors for the records an activity or task refers to.</summary>
public static class LinkedRecordErrors
{
    public static Error Required { get; } =
        Error.Failure("LinkedRecord.Required", "Bitte einen Kontakt, eine Organisation oder eine Anfrage angeben.");

    public static Error ContactNotFound { get; } =
        Error.NotFound("LinkedRecord.ContactNotFound", "Der Kontakt wurde nicht gefunden.");

    public static Error OrganizationNotFound { get; } =
        Error.NotFound("LinkedRecord.OrganizationNotFound", "Die Organisation wurde nicht gefunden.");

    public static Error OpportunityNotFound { get; } =
        Error.NotFound("LinkedRecord.OpportunityNotFound", "Die Anfrage wurde nicht gefunden.");
}

internal static class LinkedRecordRules
{
    /// <summary>Checks that all referenced records exist (archived ones included).</summary>
    public static async Task<Error?> CheckAsync(ICrmDbContext db, LinkedRecords linkedTo, CancellationToken cancellationToken)
    {
        if (linkedTo.ContactId is { } contactId && !await db.Contacts.AnyAsync(c => c.Id == contactId, cancellationToken))
        {
            return LinkedRecordErrors.ContactNotFound;
        }

        if (linkedTo.OrganizationId is { } organizationId && !await db.Organizations.AnyAsync(o => o.Id == organizationId, cancellationToken))
        {
            return LinkedRecordErrors.OrganizationNotFound;
        }

        if (linkedTo.OpportunityId is { } opportunityId && !await db.Opportunities.AnyAsync(o => o.Id == opportunityId, cancellationToken))
        {
            return LinkedRecordErrors.OpportunityNotFound;
        }

        return null;
    }
}
