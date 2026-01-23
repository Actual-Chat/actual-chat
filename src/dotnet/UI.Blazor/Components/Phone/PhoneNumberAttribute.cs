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
            return validationContext.Error(ErrorMessage ?? "Phone number is incorrect");

        if (IsOptional && sValue.IsNullOrEmpty())
            return ValidationResult.Success;

        if (!sValue.StartsWith("+", StringComparison.OrdinalIgnoreCase))
            return validationContext.Error(ErrorMessage ?? "Phone number must start with '+'");

        if (!sValue.Skip(1).All(IsValidCharacter))
            return validationContext.Error(ErrorMessage ?? "Phone number contains invalid characters");

        var digitCount = sValue.Skip(1).Count(char.IsDigit);
        switch (digitCount) {
        case < 8:
            return validationContext.Error(ErrorMessage ?? "Phone number is too short");
        case > 15:
            return validationContext.Error(ErrorMessage ?? "Phone number is too long");
        }

        var phones = validationContext.GetRequiredService<IPhones>();
        var phone = await phones.Parse(sValue, cancellationToken);
        return phone != null
            ? ValidationResult.Success
            : validationContext.Error(ErrorMessage ?? "Phone number is incorrect");
    }

    private static bool IsValidCharacter(char c)
        => char.IsDigit(c) || c == '-' || c == '(' || c == ')' || c == ' ';
}
