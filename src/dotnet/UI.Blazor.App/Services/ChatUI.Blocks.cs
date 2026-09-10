namespace ActualChat.UI.Blazor.App.Services;

public partial class ChatUI
{
    // Protected/internal methods

    internal static IEnumerable<Range<long>> NormalizeBlockRanges(
        IEnumerable<Range<long>> ranges, ConversationId? liveBlockId, ConversationId? materializedBlockId)
        // Reserves live ownership even when its record is missing. After materialization, only the card ID
        // is reserved here, so later conversations can still take over.
        => (liveBlockId is { } id
                ? ranges.Append(new(id.StartEntryLid,
                    materializedBlockId == null ? long.MaxValue : id.StartEntryLid + 1))
                : ranges)
            .TruncateOverlaps(mustKeepOpenEnded: true);

    internal static Conversation[] ResolveBlockAliases(
        IEnumerable<Conversation> conversations, ConversationViewState view)
    {
        // Keeps the live render ID after materialization, preferring the persisted record. Retains live coverage
        // when stale metadata has clipped the persisted record before that ID.
        var records = conversations.ToList();
        if (view.DissolvingConversation is { } dissolvingConversation) {
            records.RemoveAll(c => c.Id == dissolvingConversation.Id);
            records.Add(dissolvingConversation);
        }
        if (view.MaterializedBlockId is not { } materializedId || view.LiveBlockConversationId is not { } renderId
            || records.All(c => c.Id != materializedId))
            return records.ToArray();

        var liveEnd = records.FirstOrDefault(c => c.Id == renderId)?.EndEntryLid;
        return records.Where(c => c.Id != renderId || c.Id == materializedId)
            .Select(c => c.Id == materializedId ? c with {
                Id = renderId,
                EndEntryLid = c.EndEntryLid < renderId.StartEntryLid ? liveEnd ?? c.EndEntryLid : c.EndEntryLid,
            } : c)
            .DistinctBy(c => c.Id)
            .ToArray();
    }

    internal static ConversationViewState TruncateMaterializedRanges(
        ConversationViewState view, IEnumerable<Range<long>> ranges)
    {
        // Stops a materialized block's fold and hidden tail at its successor; active live ownership is unchanged.
        if (view.LiveBlockConversationId is not { } liveId || view.MaterializedBlockId == null)
            return view;

        var nextStart = ranges.Where(r => r.Start > liveId.StartEntryLid)
            .Select(r => r.Start).DefaultIfEmpty(long.MaxValue).Min();
        var coverage = new Range<long>(liveId.StartEntryLid, nextStart);
        return view with {
            LiveFoldRange = view.LiveFoldRange.IntersectWith(coverage),
            HiddenLiveTailRange = view.HiddenLiveTailRange.IntersectWith(coverage),
        };
    }

    internal static List<ChatBlock> BuildChatBlocks(
        ChatId chatId,
        IReadOnlyList<Range<long>> ranges,
        IEnumerable<Conversation?> conversations,
        ConversationViewState view,
        Conversation? liveConversation,
        Range<long> liveBlockRange)
    {
        // Resolves ownership before bounding open-ended coverage for rendering. Missing records still reserve
        // ownership but do not produce cards.
        if (!view.ShowConversations)
            return [];

        var byId = conversations.OfType<Conversation>().DistinctBy(c => c.Id).ToDictionary(c => c.Id);
        var blocks = new List<ChatBlock>();
        foreach (var range in ranges) {
            var id = ConversationId.New(chatId, range.Start);
            if (id == view.LiveBlockConversationId || id == view.MaterializedBlockId)
                continue;
            if (!byId.TryGetValue(id, out var conversation))
                continue;

            blocks.Add(new(conversation, range, view.ExpandedConversations.Contains(id), false));
        }

        var blockConversation = view.MaterializedBlockId is { } materializedId
            ? byId.GetValueOrDefault(materializedId)
            : view.DissolvingConversation ?? liveConversation;
        if (view.LiveBlockConversationId is { } renderId && blockConversation != null && !liveBlockRange.IsEmpty) {
            if (blockConversation.Id != renderId)
                blockConversation = blockConversation with { Id = renderId };
            blocks.Add(new(blockConversation, liveBlockRange,
                view.ExpandedConversations.Contains(renderId), view.MaterializedBlockId == null));
        }

        var byStart = blocks.ToDictionary(b => b.EntryLidRange.Start);
        var candidates = ranges.Where(r => ConversationId.New(chatId, r.Start) != view.MaterializedBlockId
                && ConversationId.New(chatId, r.Start) != view.LiveBlockConversationId)
            .Concat(blocks.Select(b => b.EntryLidRange));
        return NormalizeBlockRanges(candidates, view.LiveBlockConversationId, view.MaterializedBlockId)
            .Where(r => byStart.ContainsKey(r.Start))
            .Select(r => r.IsOpenEnded ? byStart[r.Start].EntryLidRange : r)
            .Select(r => byStart[r.Start] with {
                EntryLidRange = r,
                Conversation = byStart[r.Start].Conversation with { EndEntryLid = r.End - 1 },
            })
            .ToList();
    }

    // Private methods

    private async Task<List<ChatBlock>> GetChatBlocks(
        ChatId chatId,
        IList<ChatRangeTile> rangeTiles,
        IReadOnlyDictionary<ConversationId, Conversation> knownConversations,
        ConversationViewState view,
        Conversation? liveConversation,
        Range<long> liveBlockRange,
        CancellationToken cancellationToken)
    {
        if (!view.ShowConversations)
            return [];

        var ranges = rangeTiles.SelectMany(m => m.ConversationRanges).EnsureMonotonic().ToList();
        var ids = ranges.Select(r => ConversationId.New(chatId, r.Start)).ToList();
        if (view.MaterializedBlockId is { } materializedId)
            ids.Add(materializedId);

        var conversations = await ids.Distinct()
            .Where(id => id != view.LiveBlockConversationId || id == view.MaterializedBlockId)
            .Select(id => knownConversations.TryGetValue(id, out var conversation)
                ? Task.FromResult<Conversation?>(conversation)
                : Conversations.Get(Session, id, cancellationToken))
            .Collect(ApiConstants.Concurrency.High, cancellationToken)
            .ConfigureAwait(false);
        return BuildChatBlocks(chatId, ranges, conversations, view, liveConversation, liveBlockRange);
    }
}
