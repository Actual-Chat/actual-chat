namespace ActualChat.Transcription;

/// <summary>
/// <see cref="TranscriptionContext"/> rendered down to a specific transcriber's budget.
/// </summary>
public readonly record struct TranscriptionContextText(string Summary, string Text)
{
    public static readonly TranscriptionContextText None = new("", "");
    public bool IsNone => Summary.IsNullOrEmpty() && Text.IsNullOrEmpty();
}
