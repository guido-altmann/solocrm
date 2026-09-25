using FluentValidation.Results;

namespace SoloCrm.Application.Abstractions;

public static class ValidationExtensions
{
    /// <summary>
    /// Groups the failures of a FluentValidation result by property name.
    /// </summary>
    public static ValidationError ToValidationError(this ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var errors = result.Errors
            .GroupBy(f => f.PropertyName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).Distinct().ToArray(), StringComparer.Ordinal);

        return new ValidationError(errors);
    }
}
