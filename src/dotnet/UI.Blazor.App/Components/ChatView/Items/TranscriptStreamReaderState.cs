namespace ActualChat.UI.Blazor.App.Components;

public sealed record TranscriptStreamReaderState(
    string RetainedText,
    string ChangedText,
    string AnimatedText,
    string Tail,
    bool IsStreaming,
    bool IsTranslating)
{
    public static readonly TranscriptStreamReaderState None = new("", "", "", "", false, false);
}
