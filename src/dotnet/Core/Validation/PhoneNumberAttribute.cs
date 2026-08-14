using System.ComponentModel.DataAnnotations;

namespace ActualChat.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class PhoneNumberAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var errorKey = Validators.Phone.Validate(value as string);
        return errorKey is null
            ? ValidationResult.Success
            : validationContext.Error(ErrorMessage ?? errorKey);
    }
}
