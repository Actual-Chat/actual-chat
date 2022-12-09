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
    public static readonly ImmutableDictionary<string, LanguageId> Map =
            ImmutableDictionary<string, LanguageId>.Empty
                .WithComparers(StringComparer.OrdinalIgnoreCase)
                .SetItems(All.Select(x => KeyValuePair.Create(x.Code, x)))
                .SetItems(All.Select(x => KeyValuePair.Create(x.Value, x)));

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

    public string Code
        => Value switch {
            "en-US" => "EN",
            "fr-FR" => "FR",
            "de-DE" => "DE",
            "ru-RU" => "RU",
            "es-ES" => "ES",
            "uk-UA" => "UK",
            _ => "?",
        };

    public LanguageId RequireValid()
        => IsValid ? this : throw InvalidLanguageIdError();

    public LanguageId Or(LanguageId alternative)
        => IsValid ? this : alternative;

    private Exception InvalidLanguageIdError()
        => new InvalidOperationException("Invalid LanguageId.");
}
