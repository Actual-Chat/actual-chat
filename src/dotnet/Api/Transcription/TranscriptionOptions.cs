namespace ActualChat.Transcription;

public record TranscriptionOptions
{
    public Language Language { get; init; } = Languages.Main;
    public bool DetectLanguage { get; init; } = false;
    public Language[] LanguageCandidates { get; init; } = [];
    public Action<Language[]>? LanguageDetectedCallback { get; init; }
}
