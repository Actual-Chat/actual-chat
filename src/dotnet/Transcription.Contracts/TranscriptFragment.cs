namespace ActualChat.Transcription;

public record TranscriptFragment
{
    public int Index { get; init; }
    public double StartOffset { get; init; }
    public double Duration { get; init; }
    public string Text { get; init; } = "";

    public int TextIndex { get; init; } = 0;
    public string SpeakerId { get; init; } = "";
    public double Confidence { get; init; } = 1;
    public bool IsFinal { get; init; }

    public void Deconstruct(
        out int index,
        out double startOffset,
        out double duration,
        out string text,
        out int textIndex,
        out bool isFinal)
    {
        index = Index;
        startOffset = StartOffset;
        duration = Duration;
        text = Text;
        textIndex = TextIndex;
        isFinal = IsFinal;
    }
}
