using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace ActualChat.Localization;

/// <summary>
/// Maps stock <see cref="ValidationAttribute"/> types to <see cref="ValidationStrings"/> keys,
/// so models can keep plain <c>[Required]</c> / <c>[StringLength]</c> annotations.
/// </summary>
public static class ValidationMessageLocalizer
{
    private static readonly IReadOnlyDictionary<Type, string?> FrameworkDefaultMessages =
        new Dictionary<Type, string?> {
            [typeof(EmailAddressAttribute)] = new EmailAddressAttribute().ErrorMessage,
            [typeof(PhoneAttribute)] = new PhoneAttribute().ErrorMessage,
            [typeof(UrlAttribute)] = new UrlAttribute().ErrorMessage,
        };

    public static string? Localize(ValidationAttribute attribute, string fieldName)
    {
        var (key, args) = Describe(attribute);
        if (key is null)
            return null;

        var format = ValidationStrings.GetOrDefault(key);
        return format is null
            ? null
            : string.Format(CultureInfo.InvariantCulture, format, [fieldName, ..args]);
    }

    // DataTypeAttribute-derived attributes pre-populate ErrorMessage in their constructor,
    // so a non-null ErrorMessage alone doesn't mean the developer set one.
    public static bool HasExplicitMessage(ValidationAttribute attribute)
    {
        if (attribute.ErrorMessageResourceName is not null)
            return true;
        if (attribute.ErrorMessage is not { } message)
            return false;

        return !FrameworkDefaultMessages.TryGetValue(attribute.GetType(), out var defaultMessage)
            || !string.Equals(message, defaultMessage, StringComparison.Ordinal);
    }

    public static string GetFieldName(string? propertyName, string fallback)
    {
        if (propertyName.IsNullOrEmpty())
            return fallback;

        return ValidationStrings.GetOrDefault($"Field_{propertyName}") ?? fallback;
    }

    // Private methods

    // Argument order matches each attribute's own message format, so the English .resx
    // values can stay byte-identical to the framework's.
    private static (string? Key, object?[] Args) Describe(ValidationAttribute attribute)
        => attribute switch {
            RequiredAttribute => ("Validation_Required", []),
            StringLengthAttribute a => ("Validation_StringLength", [a.MaximumLength, a.MinimumLength]),
            MinLengthAttribute a => ("Validation_MinLength", [a.Length]),
            MaxLengthAttribute a => ("Validation_MaxLength", [a.Length]),
            RangeAttribute a => ("Validation_Range", [a.Minimum, a.Maximum]),
            EmailAddressAttribute => ("Validation_EmailAddress", []),
            PhoneAttribute => ("Validation_Phone", []),
            UrlAttribute => ("Validation_Url", []),
            CompareAttribute a => ("Validation_Compare", [a.OtherProperty]),
            RegularExpressionAttribute a => ("Validation_RegularExpression", [a.Pattern]),
            _ => (null, []),
        };
}
