namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Scoped holder of the current UI language, read synchronously by the string localizer.
/// Seeded and persisted by <see cref="LanguageUI"/> (local settings).
/// </summary>
// TODO: why do we need it at all? Why do we need separate a separate class for state?
public sealed class UILanguageState
{
    public static readonly string[] SupportedLanguages = ["en", "es", "fr", "it", "ru", "de"];
    public static readonly HashSet<string> SupportedLanguageSet = [..SupportedLanguages];
    public const string DefaultLanguage = "en";
    public const string KvasKey = "UILanguage";

    public string Language { get; set; } = DefaultLanguage;

    public static bool IsSupported(string? language)
        => language != null && SupportedLanguageSet.Contains(language);

    public static string Normalize(string? language)
        => IsSupported(language) ? language! : DefaultLanguage;
}
