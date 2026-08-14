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
            return validationContext.Error(ValidationKeys.AliasTooShort);
        if (!AliasId.Alphabet.IsMatch(s))
            return validationContext.Error(ValidationKeys.AliasInvalidCharacters);

        return ValidationResult.Success;
    }
}
