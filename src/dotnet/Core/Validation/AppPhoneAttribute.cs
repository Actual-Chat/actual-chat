using System.ComponentModel.DataAnnotations;

namespace ActualChat.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
// TODO: better name? suggest alternatives
public sealed class AppPhoneAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var error = Validators.Phone.Validate(value as string);
        return error is null
            ? ValidationResult.Success
            : validationContext.Error(ErrorMessage ?? error);
    }
}
