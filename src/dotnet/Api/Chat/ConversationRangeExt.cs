namespace ActualChat.Chat;

public static class ConversationRangeExt
{
    extension(Range<long> range)
    {
        public bool IsOpenEnded => range.End == long.MaxValue;
    }

    public static IEnumerable<Range<long>> MergeConversationRanges(this IEnumerable<Range<long>> ranges)
    {
        // Later-starting finite ranges truncate earlier ones. The first open-ended range keeps its boundaries
        // and owns the remaining tail, suppressing ranges that start later.
        var candidates = ranges.Where(r => !r.IsEmptyOrNegative).ToList();
        var openStart = candidates.Where(r => r.IsOpenEnded)
            .Select(r => r.Start).DefaultIfEmpty(long.MaxValue).Min();
        return candidates.Where(r => r.Start <= openStart).TruncateOverlaps();
    }
}
