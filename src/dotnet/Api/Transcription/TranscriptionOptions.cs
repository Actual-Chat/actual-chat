namespace ActualChat.Transcription;

public record TranscriptionOptions
{
    public Language Language { get; init; } = Languages.Main;
    public bool DetectLanguage { get; init; }
    public Language[] LanguageCandidates { get; init; } = [];
    public Action<Language[]>? LanguageDetectedCallback { get; init; }

    public static TranscriptionOptions AutoDetectLanguage(
        Language[] languageCandidates,
        Action<Language[]>? languageDetectedCallback)
        => new () {
            DetectLanguage = true,
            LanguageCandidates = languageCandidates,
            LanguageDetectedCallback = languageDetectedCallback,
        };
}
