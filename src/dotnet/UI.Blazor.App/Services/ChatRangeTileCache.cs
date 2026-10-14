namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Per-chat memory of the last fresh <see cref="ChatRangeTile"/> list, served as a stand-in
/// while its refetch is in flight, with the tail synthesized from the fresh chat id range.
/// </summary>
public sealed class ChatRangeTileCache
{
    private readonly ConcurrentDictionary<ChatId, List<ChatRangeTile>> _lastKnown = new();

    public List<ChatRangeTile>? UseOrLastKnown(
        ChatId chatId,
        Task<ChatRangeTile[]> freshTask,
        Range<long> cidTilesRange,
        Range<long> chatLidRange)
    {
        // Serves the last fresh list while a refetch is in flight, so a rebuild triggered by a new entry
        // renders before the round-trip instead of after it. UseIfReady invalidates the enclosing computed
        // when the fresh result lands, so a build made from a stand-in is always followed by one made from
        // fresh meta.
        // The first read still awaits - standing in on nothing would render an empty chat. A failed fetch
        // also awaits: UseIfReady doesn't invalidate on failure, so standing in for one would pin the
        // stale value with no retry trigger, while awaiting propagates the error exactly as it did before.
        var computed = Computed.Current;
        if (computed == null || cidTilesRange.IsEmpty
            || freshTask.IsFaulted || freshTask.IsCanceled
            || !_lastKnown.TryGetValue(chatId, out var lastKnown)
            || lastKnown.Count == 0)
            return null;

        // UseIfReady before anything else: checking IsCompleted separately would race - a task completing
        // between the check and the hookup would serve a stand-in that nothing ever invalidates.
        if (freshTask.UseIfReady(null!, computed) != null)
            return null;

        return StandIn(lastKnown, cidTilesRange, chatLidRange);
    }

    public void Remember(ChatId chatId, List<ChatRangeTile> freshTiles)
        => _lastKnown[chatId] = freshTiles;

    public static List<ChatRangeTile>? StandIn(
        IReadOnlyList<ChatRangeTile> lastKnown,
        Range<long> cidTilesRange,
        Range<long> chatLidRange)
    {
        // The tail is synthesized from chatLidRange: GetIdRange is the subscribed new-entry signal and the
        // view just read it, so lids past the cached tail's entry ranges are known to exist before any meta
        // describes them. A lid removed meanwhile is harmless - the synthesized ranges over-claim, and
        // GetTile (the content source) simply doesn't return it.
        var first = lastKnown[0];
        var last = lastKnown[^1];
        if (first.LidRange.Start > cidTilesRange.Start && first.PreviousLidTileStart != null)
            return null; // The query reaches above the cached span; only the fresh fetch knows that part

        // Copied so the meta walk in GetChatItemsInternal can't mutate the cached list
        var result = new List<ChatRangeTile>(lastKnown);
        if (last.NextLidTileStart == null) {
            // The cached list ended at the chat's tail, so every lid past its entry ranges is a new entry
            var entryRanges = last.EntryLidRanges;
            if (entryRanges.Length == 0)
                return null; // A tail with no entries can't anchor the extension

            // Up to the chat's end, not the queried cid tiles' end: the cached tail tile may already span
            // past those tiles (the server merges neighbours until a tile holds enough rows, and a
            // collapsed conversation counts as one), and a stand-in capped there kept claiming the chat
            // ends where the cache does - an end the list pins to and follows down once the fresh
            // rebuild appends the real tail (#5323).
            var targetEnd = chatLidRange.End;
            if (targetEnd > entryRanges[^1].End)
                result[^1] = last with {
                    LidRange = new Range<long>(last.LidRange.Start, Math.Max(last.LidRange.End, targetEnd)),
                    EntryLidRanges = [..entryRanges[..^1], new Range<long>(entryRanges[^1].Start, targetEnd)],
                };
        }
        else if (last.LidRange.End < Math.Min(cidTilesRange.End, chatLidRange.End))
            return null; // The query reaches below the cached span; only the fresh fetch knows that part

        return result;
    }
}
