namespace ActualChat.Transcription;

public record TranscriptionOptions
{
    public LanguageId Language { get; init; } = LanguageId.Default;
}
