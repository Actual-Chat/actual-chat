namespace ActualChat.Transcription;

/// <summary>
/// Which transcription stages a transcriber covers, and whether an offline pass
/// after it would add anything.
/// </summary>
public enum TranscriberKind
{
    StreamOnly = 1,
    OfflineOnly = 2,
    StreamSelfRefined = 4,
}
