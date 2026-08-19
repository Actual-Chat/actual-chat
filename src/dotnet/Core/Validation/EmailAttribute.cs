using System.ComponentModel.DataAnnotations;

namespace ActualChat.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class EmailAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var errorKey = Validators.Email.Validate(value as string);
        return errorKey is null
            ? ValidationResult.Success
            : validationContext.Error(ErrorMessage ?? errorKey);
    }
}
