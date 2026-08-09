using System.ComponentModel.DataAnnotations;

namespace ActualChat.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class PhoneNumberAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var error = Validators.Phone.Validate(value as string);
        return error is null
            ? ValidationResult.Success
            : validationContext.Error(ErrorMessage ?? error);
    }
}
