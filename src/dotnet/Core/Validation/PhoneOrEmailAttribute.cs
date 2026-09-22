using System.ComponentModel.DataAnnotations;

namespace ActualChat.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class PhoneOrEmailAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string s || (s.Trim() is var input && input.IsNullOrEmpty()))
            return ValidationResult.Success; // Empty = ok, use [Required] separately

        // '@' means email; otherwise a leading digit or '+' means phone, and anything else
        // (a leading letter) is validated as a partial email — so once the user starts typing
        // the field reports a concrete email/phone error instead of the generic "phone or email".
        var errorKey = Validators.IsEmailLike(input) || !Validators.IsPhoneLike(input)
            ? Validators.Email.Validate(input)
            : Validators.Phone.Validate(input);
        return errorKey is null
            ? ValidationResult.Success
            : validationContext.Error(ErrorMessage ?? errorKey);
    }
}
