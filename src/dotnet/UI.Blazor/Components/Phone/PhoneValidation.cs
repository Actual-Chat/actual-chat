using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.Components;

public static partial class PhoneValidation
{
    public const string CountryCodePattern = @"\+[\d\s]+";
    public const string NumberPattern = @"[\d\s\(\-]+";
    public const int MinNumberLength = 7;

    public static bool IsValid(string code, string number)
        => IsCodeValid(code) && IsNumberValid(number);

    public static bool IsCodeValid(string code)
        => CodeRe().IsMatch(code) && CodeExists(code);

    public static bool IsNumberValid(string value)
        => NumberRe().IsMatch(value) && value.Where(char.IsDigit).Skip(MinNumberLength - 1).Any();

    private static bool CodeExists(string value)
        => PhoneCodes.GetByCode(value) != null;

    [GeneratedRegex(CountryCodePattern)]
    private static partial Regex CodeRe();


    [GeneratedRegex(NumberPattern)]
    private static partial Regex NumberRe();
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public class CountryCodeAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext context)
        => PhoneValidation.IsCodeValid(value as string ?? "") ? ValidationResult.Success : context.Error(ErrorMessage ?? "Country code is incorrect");
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public class PhoneNumberAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        => PhoneValidation.IsNumberValid(value as string ?? "") ? ValidationResult.Success : validationContext.Error(ErrorMessage ?? "Phone number is incorrect");
}
