using System.ComponentModel.DataAnnotations;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.Components;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class PhoneNumberAttribute : AsyncValidationAttribute
{
    public bool IsOptional { get; set; }

    public override async Task<ValidationResult?> IsValidAsync(object? value, ValidationContext validationContext, CancellationToken cancellationToken)
    {
        if (value is not string sValue)
            return new ValidationResult(ErrorMessage ?? "Your phone number is incorrect");

        if (IsOptional && sValue.IsNullOrEmpty())
            return ValidationResult.Success;

        var phones = validationContext.GetRequiredService<IPhones>();
        var phone = await phones.Parse(sValue, cancellationToken);
        return phone.IsValid
            ? ValidationResult.Success
            : validationContext.Error(ErrorMessage ?? "Your phone number is incorrect");
    }
}
