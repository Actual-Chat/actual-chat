namespace ActualChat.Transcription;

/// <summary>
/// Configures transcription settings including language and detection.
/// </summary>
public record TranscriptionOptions
{
    public Language Language { get; init; } = Languages.Main;
    public bool DetectLanguage { get; init; }
    public Language[] LanguageCandidates { get; init; } = [];
    public Action<Language[]>? LanguageDetectedCallback { get; init; }
    public TranscriptionContext Context { get; init; } = TranscriptionContext.None;

    public string[] GetLanguageHints(Func<Language, string>? toCode = null)
    {
        // Detection mode hints with the chat's candidates, which may legitimately be none.
        toCode ??= static x => x.IsoCode;
        return DetectLanguage
            ? LanguageCandidates.Select(toCode).Distinct().ToArray()
            : [toCode(Language)];
    }

    public static TranscriptionOptions AutoDetectLanguage(
        Language[] languageCandidates,
        Action<Language[]>? languageDetectedCallback)
        => new () {
            DetectLanguage = true,
            LanguageCandidates = languageCandidates,
            LanguageDetectedCallback = languageDetectedCallback,
        };
}
