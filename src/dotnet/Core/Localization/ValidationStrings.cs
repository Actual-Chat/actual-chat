using System.Resources;

namespace ActualChat.Localization;

// Hand-written rather than resx-designer-generated on purpose: the generated accessor resolves
// either via CultureInfo.CurrentUICulture (always invariant here) or via its own static Culture
// field, which is process-wide and would leak one user's language into another's Blazor Server
// circuit. UILanguage.Current is AsyncLocal, so it is per-circuit and per-call safe.

/// <summary>
/// Localized validation messages and field names, resolved against <see cref="UILanguage.Current"/>.
/// </summary>
public static class ValidationStrings
{
    private static readonly ResourceManager ResourceManager = new(typeof(ValidationStrings));

    public static string Field_Email => Get(nameof(Field_Email));
    public static string Field_Name => Get(nameof(Field_Name));
    public static string Field_Phone => Get(nameof(Field_Phone));

    public static string Validation_Required => Get(nameof(Validation_Required));
    public static string Validation_StringLength => Get(nameof(Validation_StringLength));
    public static string Validation_EmailAddress => Get(nameof(Validation_EmailAddress));
    public static string Validation_PhoneOrEmail => Get(nameof(Validation_PhoneOrEmail));

    public static string Get(string name)
        => ResourceManager.GetString(name, UILanguage.Current) ?? name;
}
