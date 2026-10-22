namespace ActualChat.Transcription;

public sealed record TranscriptionOptions
{
    public Language Language { get; init; } = Languages.Main;
    public bool DetectLanguage { get; init; }
    public Language[] LanguageCandidates { get; init; } = [];
    public TranscriptionContext Context { get; init; } = TranscriptionContext.None;

    public Language[] GetExpectedLanguages()
        // The chat's language, or the speaker's spoken languages when the chat has none (possibly none at all).
        => DetectLanguage ? LanguageCandidates : [Language];

    public string[] GetLanguageHints(Func<Language, string>? toCode = null)
        => [.. GetExpectedLanguages().Select(toCode ?? (static x => x.IsoCode)).Distinct()];

    public static TranscriptionOptions AutoDetectLanguage(Language[] languageCandidates)
        => new() {
            DetectLanguage = true,
            LanguageCandidates = languageCandidates,
        };
}
