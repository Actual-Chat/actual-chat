namespace ActualChat;

public partial struct LanguageId
{
    public static LanguageId English { get; } = new("en-US");
    public static LanguageId French { get; } = new("fr-FR");
    public static LanguageId German { get; } = new("de-DE");
    public static LanguageId Russian { get; } = new("ru-RU");
    public static LanguageId Spanish { get; } = new("es-ES");
    public static LanguageId Ukrainian { get; } = new("uk-UA");
    public static LanguageId Default { get; } = English;

    public static LanguageId[] All { get; } = {
        English,
        French,
        German,
        Russian,
        Spanish,
        Ukrainian,
    };

    public bool IsValid
        => Value switch {
            "en-US" => true,
            "fr-FR" => true,
            "de-DE" => true,
            "ru-RU" => true,
            "es-ES" => true,
            "uk-UA" => true,
            _ => false,
        };

    public string Title
        => Value switch {
            "en-US" => "English",
            "fr-FR" => "French",
            "de-DE" => "German",
            "ru-RU" => "Russian",
            "es-ES" => "Spanish",
            "uk-UA" => "Ukrainian",
            _ => "Unknown",
        };

    public string Shortcut
        => Value switch {
            "en-US" => "EN",
            "fr-FR" => "FR",
            "de-DE" => "DE",
            "ru-RU" => "RU",
            "es-ES" => "ES",
            "uk-UA" => "UK",
            _ => "?",
        };

    // TODO: use Settings instead
    public LanguageId Next()
        => Value switch {
            "en-US" => Russian,
            "ru-RU" => English,
            _ => Default,
        };

    public LanguageId ValidOrDefault()
        => IsValid ? this : Default;

    public LanguageId Validate()
        => IsValid ? this : throw InvalidLanguageIdError();

    private Exception InvalidLanguageIdError()
        => new InvalidOperationException("Invalid LanguageId.");
}
