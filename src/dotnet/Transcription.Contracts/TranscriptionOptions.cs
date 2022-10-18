namespace ActualChat.Transcription;

public record TranscriptionOptions
{
    public string Language { get; init; } = LanguageId.Default;
    public bool IsPunctuationEnabled { get; init; } = true;
    public bool IsDiarizationEnabled { get; init; } = false;
    public int? MaxSpeakerCount { get; init; }
}
