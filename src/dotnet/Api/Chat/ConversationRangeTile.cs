namespace ActualChat.Chat;

[DataContract, MessagePackObject]
public sealed partial record ConversationRangeTile(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] Range<long>[] ConversationRanges,
    [property: DataMember, Key(2)] Range<long>? PreviousConversationRange,
    [property: DataMember, Key(3)] Range<long>? NextConversationRange)
{
    [IgnoreDataMember, IgnoreMember]
    public ConversationId[] ConversationIds
        => field ??= ConversationRanges.Select(r => ConversationId.New(ChatId, r.Start)).ToArray();

    public static ConversationRangeTile NewNormalized(
        ChatId chatId, Range<long> range, IEnumerable<Range<long>> candidates)
    {
        // Resolves overlap ownership before selecting this tile's ranges and nearest neighbors.
        var ranges = candidates.MergeConversationRanges().ToArray();
        return new(chatId,
            ranges.Where(r => r.Overlaps(range)).ToArray(),
            ranges.Where(r => r.End <= range.Start).Select(r => (Range<long>?)r).LastOrDefault(),
            ranges.Where(r => r.Start >= range.End).Select(r => (Range<long>?)r).FirstOrDefault());
    }

    public ConversationRangeTile ToFinite(long end, Range<long> range)
    {
        // Bounds open-ended conversations to the exclusive chat end, keeping at least their card ID.
        // Returns a new tile with overlaps and neighbors reclassified after bounding.
        var ranges = ConversationRanges.ToList();
        if (PreviousConversationRange is { } previous)
            ranges.Add(previous);
        if (NextConversationRange is { } next)
            ranges.Add(next);
        return NewNormalized(ChatId, range,
            ranges.Select(r => r.IsOpenEnded ? new Range<long>(r.Start, Math.Max(r.Start + 1, end)) : r));
    }

    public Conversation[] ApplyTo(IEnumerable<Conversation> conversations, Range<long> range)
    {
        // Copies finite coverage ends onto matching records; open-ended ranges keep the actual summary end.
        // Missing records are omitted.
        var byId = conversations.ToDictionary(c => c.Id);
        return ConversationRanges
            .Where(r => r.Overlaps(range))
            .Select(r => byId.TryGetValue(ConversationId.New(ChatId, r.Start), out var conversation)
                ? r.IsOpenEnded ? conversation : conversation with { EndEntryLid = r.End - 1 }
                : null)
            .SkipNullItems()
            .ToArray();
    }
}
