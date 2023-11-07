using System.ComponentModel.DataAnnotations;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.Components;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public class PhoneNumberAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        => PhoneFormatterExt.FromReadable(value as string ?? "").IsValid
            ? ValidationResult.Success
            : validationContext.Error(ErrorMessage ?? "Your phone number is incorrect");
}
