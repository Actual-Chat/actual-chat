using CommunityToolkit.HighPerformance;

namespace ActualChat.UI.Blazor.App.Services;

public partial class ChatUI
{
    internal static bool TryGetIdTilesToLoad(
        ConversationViewState conversationView,
        IReadOnlyList<ChatBlock> blocks,
        ChatDataQuery dataQuery1,
        IList<ChatRangeTile> chatRangeTiles,
        out List<Range<long>> idTiles1,
        out bool hasMoreBefore1,
        out bool hasMoreAfter1)
    {
        conversationView = TruncateMaterializedRanges(conversationView,
            chatRangeTiles.SelectMany(m => m.ConversationRanges).Concat(blocks.Select(b => b.EntryLidRange)));
        var showConversations = conversationView.ShowConversations;
        var hiddenLiveTailRange = conversationView.HiddenLiveTailRange;
        var liveBlockId = conversationView.LiveBlockConversationId;
        var liveBlockFoldRange = conversationView.LiveFoldRange;
        if (chatRangeTiles.Count == 0) {
            idTiles1 = [];
            hasMoreBefore1 = false;
            hasMoreAfter1 = false;
            return true;
        }

        var hasPreviousIdTile = chatRangeTiles[0].PreviousLidTileStart.HasValue;
        var hasNextIdTile = chatRangeTiles[^1].NextLidTileStart.HasValue;
        var entryIdRanges = chatRangeTiles
            .SelectMany(m => m.EntryLidRanges)
            .EnsureMonotonic();
        var collapsedBlocks = blocks.Where(b => !b.IsExpanded).ToList();
        // Coverage includes the whole live block; only its governed fold may exclude entry tiles.
        var excludedRanges = blocks.Where(b => !b.IsExpanded
                || (b.Id == liveBlockId && conversationView.MaterializedBlockId == null))
            .Select(b => b.Id == liveBlockId ? liveBlockFoldRange.IntersectWith(b.EntryLidRange) : b.EntryLidRange)
            .Where(r => !r.IsEmpty)
            .ToList();
        // Only the block's own start lid, never the span it hides: an id tile dropped here is never
        // fetched, and the messages typed during the call live in those tiles interleaved with it, so
        // any wider range takes them down with it. Hiding spoken entries is the per-entry filter's
        // job and costs nothing extra to load. This one lid is still what makes an entry-less block
        // (video-only, no summary yet) emit its card at all - liveBlockFoldRange is empty then, so
        // the select above contributes nothing for it.
        var hiddenTailToExclude = hiddenLiveTailRange.IsEmpty
            ? default
            : new Range<long>(hiddenLiveTailRange.Start, hiddenLiveTailRange.Start + 1);
        if (!hiddenTailToExclude.IsEmpty)
            excludedRanges.Add(hiddenTailToExclude);
        excludedRanges = excludedRanges.OrderBy(r => r.Start).EnsureMonotonic().ToList();

        var merged = showConversations
            ? entryIdRanges
                .Merge(excludedRanges, (ce, co) => ce.IntersectWith(co).IsEmpty ? (int)(ce.Start - co.Start) : 0)
                .ToList()
            : entryIdRanges
                .Select(idRange => (idRange, new Range<long>(0, 0)));

        var resultIdRanges = new List<Range<long>>();

        Range<long>? pendingRight = null; // right part of the current entryRange
        Range<long> currentEntryRange = default;

        foreach (var (entryRange, conversationRange) in merged) {
            var hasEntryRange = !entryRange.IsEmpty;
            var hasConversationRange = !conversationRange.IsEmpty;

            // If we start processing a NEW entryRange, flush the pending right-hand side
            var conversationStartRange = new Range<long>(conversationRange.Start, conversationRange.Start + 1);
            if (hasEntryRange) {
                if (entryRange == currentEntryRange && hasConversationRange) {
                    var (l, r) = (pendingRight ?? default).Subtract(conversationRange);
                    AddRange(resultIdRanges, l);
                    AddRange(resultIdRanges, conversationStartRange);
                    pendingRight = r;
                }
                else {
                    AddRange(resultIdRanges, pendingRight ?? default);
                    pendingRight = null;
                    currentEntryRange = entryRange;
                }
            }

            if (hasEntryRange && hasConversationRange) {
                if (conversationRange.Contains(entryRange))
                    AddRange(resultIdRanges, conversationStartRange);
                else {
                    var (l, r) = entryRange.Subtract(conversationRange);
                    AddRange(resultIdRanges, l);
                    AddRange(resultIdRanges, conversationStartRange);
                    pendingRight = r;
                }
            }
            else if (hasEntryRange)
                AddRange(resultIdRanges, entryRange);
            else if (hasConversationRange) {
                // A card-only excluded range (e.g. the live block's hidden tail, which has no paired
                // entry range) that arrives after an entry range left a pendingRight must flush that
                // remainder FIRST. Otherwise its card is appended ahead of pendingRight, and AddRange's
                // monotonic guard then silently drops the out-of-order pendingRight - its entries vanish,
                // leaving a gap in the loaded tiles (a stale conversation card glued to the live block).
                AddRange(resultIdRanges, pendingRight ?? default);
                pendingRight = null;
                AddRange(resultIdRanges, conversationStartRange);
            }
        }
        AddRange(resultIdRanges, pendingRight ?? default);

        var resultIdRangesSpan = resultIdRanges.AsSpan();
        var startIdWithOffset = GetIdWithOffset(
            resultIdRangesSpan,
            dataQuery1.ExistingLidRange.Start,
            dataQuery1.StartOffset);

        var endIdWithOffset = GetIdWithOffset(
            resultIdRangesSpan,
            dataQuery1.ExistingLidRange.End,
            dataQuery1.EndOffset);

        var hasFulfilledStart = (startIdWithOffset != null
                && HasOffsetReached(dataQuery1.StartOffset, startIdWithOffset.Value.ActualOffset))
            || !hasPreviousIdTile;
        var hasFulfilledEnd = (endIdWithOffset != null
                && HasOffsetReached(dataQuery1.EndOffset, endIdWithOffset.Value.ActualOffset))
            || !hasNextIdTile;
        var startEntryLid = startIdWithOffset?.Id ?? 0L;
        var endEntryLid = endIdWithOffset?.Id ?? long.MaxValue;
        // Keep the loaded range covering the visible range even when the scroll-driven offsets would
        // contract past it (they're derived from coordinate gaps ÷ average item size, which misfires
        // next to a very large item) — dropping a visible item would drop the scroll anchor and jump.
        var visibleLidRange = dataQuery1.VisibleLidRange;
        if (!visibleLidRange.IsEmpty) {
            startEntryLid = Math.Min(startEntryLid, visibleLidRange.Start);
            endEntryLid = Math.Max(endEntryLid, visibleLidRange.End);
        }
        if (showConversations) {
            var range = ExpandToCollapsedBlocks(new(startEntryLid, endEntryLid), collapsedBlocks);
            startEntryLid = range.Start;
            endEntryLid = range.End;
            hasFulfilledStart &= !hasPreviousIdTile || startEntryLid >= chatRangeTiles[0].LidRange.Start;
            hasFulfilledEnd &= !hasNextIdTile || endEntryLid < chatRangeTiles[^1].LidRange.End;
        }

        idTiles1 = resultIdRanges
            .SkipWhile(r => r.End <= startEntryLid)
            .TakeWhile(r => r.Start <= endEntryLid)
            .SelectMany(r => EntryIdTiles.GetCoveringTiles(r).Select(t => t.Range))
            .SkipWhile(r => r.End <= startEntryLid)
            .TakeWhile(r => r.Start <= endEntryLid)
            .EnsureMonotonic()
            .ToList();

        hasMoreBefore1 = hasPreviousIdTile
            || (hasFulfilledStart && idTiles1.Count > 0 && idTiles1[0].Start > resultIdRanges[0].Start);
        hasMoreAfter1 = hasNextIdTile
            || (hasFulfilledEnd && idTiles1.Count > 0 && idTiles1[^1].End < resultIdRanges[^1].End);
        return hasFulfilledStart && hasFulfilledEnd;

        static void AddRange(List<Range<long>> list, Range<long> range) {
            if (range.IsEmpty)
                return;

            if (list.Count == 0 || list[^1].End <= range.Start)
                list.Add(range);
        }

        static bool HasOffsetReached(long offset, long actualOffset) {
            if (offset < 0)
                return actualOffset <= offset;

            return actualOffset >= offset;
        }
    }

    // Private methods

    private static Range<long> ExpandToCollapsedBlocks(
        Range<long> inclusiveRange, IReadOnlyList<ChatBlock> blocks)
    {
        if (inclusiveRange.IsNegative)
            return inclusiveRange;

        var previous = inclusiveRange;
        do {
            previous = inclusiveRange;
            foreach (var item in blocks) {
                var block = item.EntryLidRange;
                if (block.IsEmptyOrNegative || block.IsOpenEnded)
                    continue;
                if (block.Start > inclusiveRange.End || block.End <= inclusiveRange.Start)
                    continue;

                // Query endpoints are inclusive; metadata and block coverage use an exclusive end.
                inclusiveRange = inclusiveRange.MinMaxWith(new Range<long>(block.Start, block.End - 1));
            }
        } while (inclusiveRange != previous);

        return inclusiveRange;
    }

    private static (long Id, int ActualOffset)? GetIdWithOffset(
        ReadOnlySpan<Range<long>> ranges,
        long anchorId,
        int requestedOffset)
    {
        if (ranges.IsEmpty)
            return null;

        if (requestedOffset == 0)
            return (anchorId, 0);

        var isForward = requestedOffset > 0;
        var index = ranges.BinarySearch(r => r.End > anchorId);
        if (index < 0) {
            if (isForward)
                return null;

            index = ranges.Length - 1;
        }

        var remaining = Math.Abs((long)requestedOffset);
        var travelled = 0L;

        var currentId = anchorId;
        while (remaining > 0) {
            var r = ranges[index];

            if (isForward) {
                // start position inside this range
                var begin = Math.Max(r.Start, currentId + 1);
                var capacity = r.End - begin;

                if (capacity <= 0) {
                    if (++index >= ranges.Length)
                        break;

                    continue;
                }

                if (remaining <= capacity) {
                    currentId = begin + remaining - 1;
                    travelled += remaining;
                    remaining = 0;
                }
                else {
                    currentId = r.End - 1; // last item of this range
                    travelled += capacity;
                    remaining -= capacity;
                    if (++index >= ranges.Length)
                        break;
                }
            }
            else {
                var end = Math.Min(r.End - 1, currentId - 1);
                var capacity = end - r.Start + 1;

                if (capacity <= 0) {
                    if (--index < 0)
                        break;

                    continue;
                }

                if (remaining <= capacity) {
                    currentId = end - remaining + 1;
                    travelled += remaining;
                    remaining = 0;
                }
                else {
                    currentId = r.Start;
                    travelled += capacity;
                    remaining -= capacity;
                    if (--index < 0)
                        break;
                }
            }
        }

        var actualOffset = (int)(isForward ? travelled : -travelled);
        return (currentId, actualOffset);
    }
}
