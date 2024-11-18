using System.ComponentModel.DataAnnotations;

namespace ActualChat.UI.Blazor.App.Components;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class UserLinkIdAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var str = value as string;
        if (str.IsNullOrEmpty())
            return ValidationResult.Success;

        if (str.Length < 5)
            return new ValidationResult("Link is too short.", GetMemberNames(validationContext));

        if (!UserLinkId.Alphabet.IsMatch(str))
            return new ValidationResult("Link should contain only 0-9, a-Z, and _.", GetMemberNames(validationContext));

        return ValidationResult.Success;
    }

    private static string[]? GetMemberNames(ValidationContext validationContext)
    {
        if (validationContext.MemberName is { } memberName)
            return [memberName];

        return null;
    }
}
