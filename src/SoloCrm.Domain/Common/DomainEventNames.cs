using System.Text;

namespace SoloCrm.Domain.Common;

/// <summary>
/// Public names of domain events as used in the outbox and in webhook payloads (SPEC 2.6, SPEC 5).
/// </summary>
public static class DomainEventNames
{
    /// <summary>
    /// Derives the public event name from the CLR type: the first word names the aggregate,
    /// the rest is snake_case (<c>OpportunityStageChanged</c> → <c>opportunity.stage_changed</c>).
    /// </summary>
    public static string Of(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        var name = eventType.Name;
        var builder = new StringBuilder(name.Length + 4);
        var separator = '.';
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (i > 0 && char.IsUpper(c))
            {
                builder.Append(separator);
                separator = '_';
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }
}
