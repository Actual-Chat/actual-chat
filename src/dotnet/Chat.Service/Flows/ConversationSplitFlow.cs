using ActualChat.Chat.ML;
using ActualChat.Chat.Module;
using ActualChat.Flows;
using ActualChat.Queues;
using MemoryPack;

namespace ActualChat.Chat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class ConversationSplitFlow : LegacyFlow, IHasLastRunAt
{
    public static class FlowSteps
    {
        public static readonly Symbol OnReset = nameof(ConversationSplitFlow.OnReset);
        public static readonly Symbol OnIndex = nameof(ConversationSplitFlow.OnIndex);
    }

    private const int BatchSize = 100;
    private static readonly TileStack<long> IdTileStack = Constants.Chat.ServerIdTileStack;
    [field: AllowNull, MaybeNull]
    private ChatId ChatId => field ??= ChatId.Parse(Id.Arguments);

    [field: AllowNull, MaybeNull]
    private ChatSettings Settings => field ??= Host.Services.GetRequiredService<ChatSettings>();

    [field: AllowNull, MaybeNull]
    private IChatsBackend ChatsBackend => field ??= Host.Services.GetRequiredService<IChatsBackend>();

    [field: AllowNull, MaybeNull]
    private IConversationsBackend ConversationsBackend => field ??= Host.Services.GetRequiredService<IConversationsBackend>();

    [field: AllowNull, MaybeNull]
    private IEntryGroupExtractor EntryGroupExtractor => field ??= Host.Services.GetRequiredKeyedService<IEntryGroupExtractor>(EntryGroupLimit.None);

    // Flow state

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public ExtractorState? ExtractorState { get; protected set; }

    [DataMember(Order = 100), MemoryPackOrder(100)]
    public long Cursor { get; protected set; }

    [DataMember(Order = 104), MemoryPackOrder(104)]
    public Moment LastRunAt { get; protected set; }

    [DataMember(Order = 105), MemoryPackOrder(105), Obsolete("Deprecated.")]
    public Moment? NextRecheckAt { get; protected set; }

    [DataMember(Order = 106), MemoryPackOrder(106)]
    public Moment LastSummaryAt { get; protected set; }

    [DataMember(Order = 107), MemoryPackOrder(107)]
    public Range<long>[] LastSummaryRanges { get; protected set; } = [];

    // Flow transitions

    protected override async Task<LegacyFlowTransition> OnReset(CancellationToken cancellationToken)
    {
        var chat = await ChatsBackend.Get(ChatId, cancellationToken).Require().ConfigureAwait(false);
        if (chat.IsSummarized ?? false) // Only process summarized chats
            return Resume(nameof(OnIndex));

        return WaitForEvent(FlowSteps.OnReset);
    }

    protected virtual async Task<LegacyFlowTransition> OnIndex(CancellationToken cancellationToken)
    {
        var now = Clocks.CoarseSystemClock.Now;
        LastRunAt = now;
        var lastLid = Cursor;
        var chatId = ChatId;
        Log.LogDebug("`{Id}`.OnIndex: Started at cursor {LastLid}", Id, lastLid);

        var (entries, hasMore, hasImmature) = await GetEntries(lastLid, cancellationToken).ConfigureAwait(false);
        var (state, groups, replySequences) = EntryGroupExtractor.ExtractGroups(ExtractorState ?? new ExtractorState(null, null), entries);
        ExtractorState = state;
        var hasEntries = entries.Count > 0;

        // process replies
        foreach (var replySequence in replySequences) {
            var firstEntry = replySequence.Entries[0];
            if (firstEntry.RepliedEntryLid is not { } entryLid)
                continue; // First entry in the sequence is not a reply!

            var lastEntry = replySequence.Entries[^1];
            var idTileRange = IdTileStack.LastLayer.GetTile(entryLid).Range;
            var conversationTile = await ConversationsBackend.GetRangeMeta(chatId, idTileRange.Start, cancellationToken).ConfigureAwait(false);
            var existingConversationIds = conversationTile.ConversationIds;
            var appendReply = new ConversationBackend_AppendReply(
                chatId,
                entryLid,
                new Range<long>(firstEntry.LocalId, lastEntry.LocalId + 1)
            ) {
                DelayUntil = existingConversationIds.Length == 0
                    ? now + (2 * Settings.ChatEntrySummarizationDelay)
                    : default,
            };
            await Host.Services.Queues().Enqueue(appendReply, cancellationToken).ConfigureAwait(false);
        }

        if (groups.Count == 0 && hasMore) {
            // No groups found, but we have more entries to process
            Event.MarkHandled();
            if (entries.Count > 0)
                Cursor = entries[^1].LocalId;

            // Continue immediately to process the next batch
            return StoreAndResume(FlowSteps.OnIndex, "Continue processing next batch");
        }

        foreach (var group in groups) {
            if (group.WordCount < Settings.MinConversationWords)
                continue;

            if (group.Entries.Count < Settings.MinConversationEntries)
                continue;

            var idRanges = group.LocalIdRanges;
            var summarize = new ConversationBackend_Summarize(chatId, [.. idRanges]);
            await Host.Services.Queues().Enqueue(summarize, cancellationToken).ConfigureAwait(false);
            LastSummaryAt = now;
            LastSummaryRanges = idRanges.ToArray();
        }

        // If we reached the end of the entries, we might want to summarize the last group
        if (!hasMore) {
            var lastRanges = LastSummaryRanges;
            var currentRanges = new EntryGroupBuilder(state.CurrentGroup)
                .AddRange(state.CurrentChunk?.Entries ?? [])
                .Build().LocalIdRanges;
            var rangesAreEqual = lastRanges.SequenceEqual(currentRanges);
            var hasCurrentRanges = currentRanges.Count > 0;

            var tooOften = LastSummaryAt + Settings.ChatEntrySummarizationDelay >= now;
            var readyToSummarize =
                state.CurrentGroup != null
                && state.WordCount >= Settings.MinConversationWords
                && state.EntryCount >= Settings.MinConversationEntries
                && !rangesAreEqual;

            if (hasEntries)
                Cursor = entries[^1].LocalId;
            Event.MarkHandled();

            if (readyToSummarize && !tooOften) {
                // Summarize the current group
                var groupBuilder = state.CurrentGroup!.AddRange(state.CurrentChunk?.Entries ?? []);
                var group = groupBuilder.Build();


                var idRanges = group.LocalIdRanges;
                var summarize = new ConversationBackend_Summarize(chatId, [.. idRanges]);
                await Host.Services.Queues().Enqueue(summarize, cancellationToken).ConfigureAwait(false);

                // Keep the group if there are immature items, otherwise clear the chunk
                ExtractorState = new ExtractorState(
                    hasImmature ? groupBuilder : new EntryGroupBuilder(),
                    new EntryGroupBuilder()
                );
                LastSummaryAt = now;
                LastSummaryRanges = idRanges.ToArray();
            }

            // Important: if there are immature entries in the current window, schedule a timer instead of waiting for an external event
            return hasImmature || (tooOften && !rangesAreEqual && hasCurrentRanges)
                ? WaitForTimer(FlowSteps.OnIndex, Settings.ChatEntrySummarizationDelay)
                : WaitForEvent(FlowSteps.OnIndex);
        }

        if (hasEntries)
            Cursor = entries[^1].LocalId;
        Event.MarkHandled();
        return hasMore
            ? StoreAndResume(FlowSteps.OnIndex, "Continue processing next batch")
            : hasImmature
                ? WaitForTimer(FlowSteps.OnIndex, Settings.ChatEntrySummarizationDelay)
                : WaitForEvent(FlowSteps.OnIndex);
    }

    // Private methods

    private async Task<(IReadOnlyList<TextEntry> Entries, bool HasMore, bool HasImmatureInWindow)> GetEntries(
        long lastId,
        CancellationToken cancellationToken)
    {
        var chatId = ChatId;
        var now = Clocks.CoarseSystemClock.Now;
        var immatureMoment = now - Settings.ChatEntrySummarizationDelay;

        // Fetch up to BatchSize + 1 items
        var entries = await ChatsBackend.ListNewEntries(chatId, lastId, BatchSize + 1, cancellationToken).ConfigureAwait(false);

        // Detect the first immature entry index within the fetched window
        var firstImmatureIndex = -1;
        for (var i = 0; i < entries.Length; i++) {
            var ts = entries[i].EndsAt ?? entries[i].BeginsAt;
            if (ts <= immatureMoment)
                continue;

            firstImmatureIndex = i;
            break;
        }

        var maturedCount = firstImmatureIndex >= 0 ? firstImmatureIndex : entries.Length;
        var take = Math.Min(maturedCount, BatchSize);
        var textEntries = entries
            .Take(take)
            .Select(e => new TextEntry(e))
            .ToList();

        var hasMore = entries.Length > BatchSize;   // More pages exist
        var hasImmature = firstImmatureIndex >= 0;  // At least one immature entry in this window

        return (textEntries, hasMore, hasImmature);
    }
}
