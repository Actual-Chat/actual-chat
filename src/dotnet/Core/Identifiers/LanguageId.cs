namespace ActualChat;

public partial struct LanguageId
{
    public static LanguageId English { get; } = new("en-US");
    public static LanguageId Russian { get; } = new("ru-RU");
    public static LanguageId Default { get; } = Russian;

    public bool IsValid
        => Value switch {
            "en-US" => true,
            "ru-RU" => true,
            _ => false,
        };

    public string Title
        => Value switch {
            "en-US" => "English",
            "ru-RU" => "Russian",
            _ => throw InvalidLanguageIdError(),
        };

    public LanguageId Next()
        => Value switch {
            "en-US" => Russian,
            "ru-RU" => English,
            _ => throw InvalidLanguageIdError(),
        };

    public LanguageId ValidOrDefault()
        => IsValid ? this : Default;

    public LanguageId Validate()
        => IsValid ? this : throw InvalidLanguageIdError();

    private Exception InvalidLanguageIdError()
        => new InvalidOperationException("Invalid LanguageId.");
}
