namespace ActualChat.Transcription;

/// <summary>
/// Which stage a transcriber covers. A stream may be paired with an offline
/// retranscriber; an offline one can't run on its own.
/// </summary>
public enum TranscriberKind
{
    Stream = 1,
    Offline = 2,
}

public static class TranscriberKindExt
{
    public static bool IsStream(this TranscriberKind kind)
        => kind == TranscriberKind.Stream;

    public static bool IsOffline(this TranscriberKind kind)
        => kind == TranscriberKind.Offline;
}
