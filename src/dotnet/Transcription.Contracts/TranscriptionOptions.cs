namespace ActualChat.Transcription;

public record TranscriptionOptions
{
    public Language Language { get; init; } = Languages.Main;
}
