using System.ComponentModel.DataAnnotations;

namespace VibyRoom.Api.Contracts.Validation;

[AttributeUsage(AttributeTargets.Property)]
public sealed class TrimmedRequiredStringAttribute
    : ValidationAttribute
{
    private readonly int _maxLength;

    public TrimmedRequiredStringAttribute(int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxLength);

        _maxLength = maxLength;
    }

    protected override ValidationResult? IsValid(
        object? value,
        ValidationContext validationContext)
    {
        ArgumentNullException.ThrowIfNull(validationContext);

        if (value is not string text ||
            string.IsNullOrWhiteSpace(text))
        {
            return new ValidationResult(
                $"{validationContext.DisplayName} is required.");
        }

        var normalized = text.Trim();

        if (normalized.Length > _maxLength)
        {
            return new ValidationResult(
                $"{validationContext.DisplayName} " +
                $"cannot exceed {_maxLength} characters.");
        }

        return ValidationResult.Success;
    }
}