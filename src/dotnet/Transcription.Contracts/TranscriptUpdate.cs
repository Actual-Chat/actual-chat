namespace ActualChat.Transcription;

public record TranscriptUpdate
{
    public double StartOffset { get; init; }
    public double Duration { get; init; }
    public string Text { get; init; } = "";

    public int TextIndex { get; init; } = 0;
    public string SpeakerId { get; init; } = "";
    public double Confidence { get; init; } = 1;
    public bool IsFinal { get; init; }
}
