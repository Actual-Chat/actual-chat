using System.ComponentModel.DataAnnotations;

namespace ActualChat.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class PhoneOrEmailAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string s || (s.Trim() is var input && input.IsNullOrEmpty()))
            return ValidationResult.Success; // Empty = ok, use [Required] separately

        if (Validators.IsEmailLike(input)) {
            var errorKey = Validators.Email.Validate(input);
            return errorKey is null
                ? ValidationResult.Success
                : validationContext.Error(ErrorMessage ?? errorKey);
        }

        if (Validators.IsPhoneLike(input)) {
            var errorKey = Validators.Phone.Validate(input);
            return errorKey is null
                ? ValidationResult.Success
                : validationContext.Error(ErrorMessage ?? errorKey);
        }

        return validationContext.Error(ErrorMessage ?? ValidationKeys.PhoneOrEmailRequired);
    }
}
