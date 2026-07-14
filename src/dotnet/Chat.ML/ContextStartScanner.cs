namespace ActualChat.Chat.ML;

/// <summary>
/// Finds where a live conversation's summary context should begin by walking backward from the
/// session's first entry, applying the same pause-boundary rules as <see cref="EntryGroupExtractor"/>.
/// </summary>
public static class ContextStartScanner
{
    public static long FindContextStartLid(IReadOnlyList<ChatEntrySlim> precedingEntries, ChatEntrySlim anchorEntry)
    {
        var contextStart = anchorEntry.LocalId;
        var accepted = new List<ChatEntrySlim> { anchorEntry };
        var newer = anchorEntry;
        for (var i = precedingEntries.Count - 1; i >= 0; i--) {
            var older = precedingEntries[i];
            if (older.LocalId >= newer.LocalId)
                continue;

            var currentPause = GetPause(older, newer);
            var minPause = newer.IsTranscript
                ? (int)Constants.Audio.MaxStreamDuration.TotalSeconds
                : EntryGroupExtractor.MinPauseBetweenTextEntries;
            var significantlyLargerThanAverage = AveragePause(accepted) * 10;
            var withinGroup = (currentPause < minPause || currentPause < significantlyLargerThanAverage)
                && currentPause < EntryGroupExtractor.MaxPauseBetweenEntries;
            if (!withinGroup)
                break;

            accepted.Add(older);
            contextStart = older.LocalId;
            newer = older;
        }
        return contextStart;
    }

    // Private methods

    private static int GetPause(ChatEntrySlim earlier, ChatEntrySlim later)
        => (int)Math.Max(0, (later.BeginsAt - (earlier.EndsAt ?? earlier.BeginsAt)).TotalSeconds);

    private static int AveragePause(List<ChatEntrySlim> entries)
    {
        if (entries.Count <= 1)
            return 0;

        var ordered = entries.OrderBy(e => e.LocalId).ToList();
        var total = 0;
        for (var i = 1; i < ordered.Count; i++)
            total += GetPause(ordered[i - 1], ordered[i]);
        return total / (ordered.Count - 1);
    }
}
