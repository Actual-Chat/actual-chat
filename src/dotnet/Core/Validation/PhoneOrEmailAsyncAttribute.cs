using System.ComponentModel.DataAnnotations;
using ActualChat.Localization;

namespace ActualChat.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class PhoneOrEmailAsyncAttribute : AsyncValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string s || (s.Trim() is var input && input.IsNullOrEmpty()))
            return ValidationResult.Success; // Empty = ok, use [Required] separately

        if (Validators.IsEmailLike(input)) {
            var error = Validators.Email.Validate(input);
            return error is null
                ? ValidationResult.Success
                : validationContext.Error(ErrorMessage ?? error);
        }

        if (Validators.IsPhoneLike(input)) {
            var error = Validators.Phone.Validate(input);
            return error is null
                ? ValidationResult.Success
                : validationContext.Error(ErrorMessage ?? error);
        }

        return validationContext.Error(ErrorMessage ?? ValidationStrings.Validation_PhoneOrEmail);
    }
}
