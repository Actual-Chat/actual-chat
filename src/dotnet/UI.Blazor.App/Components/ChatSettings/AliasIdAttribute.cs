using System.ComponentModel.DataAnnotations;

namespace ActualChat.UI.Blazor.App.Components;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class AliasIdAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var s = value as string;
        if (s.IsNullOrEmpty())
            return ValidationResult.Success;
        if (s.Length < 5)
            return new ValidationResult("Custom link is too short.", GetMemberNames(validationContext));
        if (!AliasId.Alphabet.IsMatch(s))
            return new ValidationResult("Custom link should contain only 0-9, a-Z, - and _.", GetMemberNames(validationContext));

        return ValidationResult.Success;
    }

    private static string[]? GetMemberNames(ValidationContext validationContext)
    {
        if (validationContext.MemberName is { } memberName)
            return [memberName];

        return null;
    }
}
