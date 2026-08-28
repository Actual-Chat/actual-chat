namespace ActualChat.UI.Blazor.App.Services;

public static class LiveFoldMath
{
    // The fold never leaves fewer than this many entries under the card: a card with one or two rows
    // below it reads as a rendering glitch rather than as a folded conversation.
    public const int MinTailEntryCount = 10;
    // Deliberately not viewport-derived: "Show more" is a fixed, predictable step, and the count is
    // spelled out on the button, so it doesn't need to guess at a screenful.
    public const int RevealBatchSize = 20;

    public static long Advance(long boundaryLid, long? minVisibleLidInBlock)
        // The collapsed live block swallows everything above the viewport. The fold boundary is the
        // topmost visible lid in the block, advanced monotonically - it never retreats when the reader
        // scrolls up, so the block stays a compact card. A null viewport (nothing of the block visible)
        // holds the current boundary. Summaries no longer gate this: un-summarised rows fold too.
        // MinTailEntryCount is applied where the boundary is read (LiveBlockUI.GetBlockState), not here,
        // so the governed value stays monotonic.
        => minVisibleLidInBlock is { } lid ? Math.Max(boundaryLid, lid) : boundaryLid;
}
