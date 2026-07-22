namespace ActualChat.UI.Blazor.App.Services;

public static class LiveFoldMath
{
    // The collapsed live block swallows everything above the viewport. The fold boundary is the
    // topmost visible lid in the block, advanced monotonically - it never retreats when the reader
    // scrolls up, so the block stays a compact card. A null viewport (nothing of the block visible)
    // holds the current boundary. Summaries no longer gate this: un-summarised rows fold too.
    public static long Advance(long boundaryLid, long? minVisibleLidInBlock)
        => minVisibleLidInBlock is { } lid ? Math.Max(boundaryLid, lid) : boundaryLid;
}
