namespace ActualChat.UI.Blazor.App.Services;

public static class LiveFoldMath
{
    // The fold never leaves fewer than this many entries under the card: a card with one or two rows
    // below it reads as a rendering glitch rather than as a folded conversation.
    public const int MinTailEntryCount = 10;
    // Deliberately not viewport-derived: "Show more" is a fixed, predictable step, and the count is
    // spelled out on the button, so it doesn't need to guess at a screenful.
    public const int RevealBatchSize = 20;

    public static long Advance(long lastFoldEndLid, long minVisibleLid, long streamingFloorLid, long tailFloorLid)
        // max(lastFoldEnd, min(viewportTop, floors)): the collapsed live block swallows everything above
        // the viewport, and only that. Every floor bounds the advance rather than capping the result, so
        // a floor lapsing - a transcript closing, a tail grown past MinTailEntryCount - can never swallow
        // a row that's on screen. The max keeps it monotonic, so nothing pops back out from under the
        // card either. minVisibleLid is 0 when no part of the block is visible, which holds the fold.
        //
        // The guarantee is per-step, not an invariant to induct on: it deliberately does NOT hold that
        // the fold end stays below the viewport top - a reveal renders rows beneath it - only that no
        // single step crosses it.
        //
        // What this shape costs, all four accepted:
        // - A floor can only bound future advances, never walk one back. Deleting entries inside the last
        //   MinTailEntryCount therefore leaves the card short of its tail permanently, where the earlier
        //   read-time cap re-opened the fold on its own.
        // - LiveBlockUI.GetTailFloorLid counts back from the chat end, which after a leave includes the
        //   entries the frozen render hides. Once enough of those pile up the floor stops constraining a
        //   frozen block at all, and its tail is only whatever the reader's viewport top left behind.
        // - The seed in LiveBlockUI.GetOrCreateChatState is bounded by the floors but by no viewport,
        //   since there isn't one yet. Re-creating that state under a still-mounted render would re-seed
        //   above the fold end it already had; only CleanupOtherChats does that, on a de-selected chat.
        // - "Never swallows a row that's on screen" means as of the last visibility report. A scroll that
        //   outruns the report can still lose a row to the advance it races - inherent to folding against
        //   a viewport, and true of every version of this rule.
        => Math.Max(lastFoldEndLid, Math.Min(minVisibleLid, Math.Min(streamingFloorLid, tailFloorLid)));
}
