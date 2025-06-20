using ActualChat.Chat.ML;
using ActualChat.Chat.Module;
using ActualChat.Flows;
using ActualChat.Queues;
using MemoryPack;

namespace ActualChat.Chat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class ConversationSplitFlow : Flow, IHasLastRunAt
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

    // Flow transitions

    protected override async Task<FlowTransition> OnReset(CancellationToken cancellationToken)
    {
        var chat = await ChatsBackend.Get(ChatId, cancellationToken).Require().ConfigureAwait(false);
        if (chat.IsSummarized ?? false) // Only process summarized chats
            return Resume(nameof(OnIndex));

        return WaitForEvent(FlowSteps.OnReset, InfiniteHardResumeAt);
    }

    protected virtual async Task<FlowTransition> OnIndex(CancellationToken cancellationToken)
    {
        LastRunAt = Clocks.SystemClock.Now;
        var lastLid = Cursor;
        var chatId = ChatId;
        Log.LogDebug("`{Id}`.OnIndex: Started at cursor {LastLid}", Id, lastLid);
        var (entries, hasMore) = await GetEntries(lastLid, cancellationToken).ConfigureAwait(false);
        var (state, groups, replySequences) = EntryGroupExtractor.ExtractGroups(ExtractorState ?? new ExtractorState(null, null), entries);
        ExtractorState = state;

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
                    ? Host.Clocks.CoarseSystemClock.Now + (2 * Settings.ChatEntrySummarizationDelay)
                    : default,
            };
            await Host.Services.Queues().Enqueue(appendReply, cancellationToken).ConfigureAwait(false);
        }


        if (groups.Count == 0 && hasMore) {
            // No groups found, but we have more entries to process
            if (entries.Count > 0)
                Cursor = entries[^1].LocalId;
            Event.MarkHandled();
            return WaitForTimer(FlowSteps.OnIndex, Settings.ChatEntrySummarizationDelay);
        }

        foreach (var group in groups) {
            if (group.WordCount < Settings.MinConversationWords)
                continue;

            if (group.Entries.Count < Settings.MinConversationEntries)
                continue;

            var idRanges = group.LocalIdRanges;
            var summarize = new ConversationBackend_Summarize(chatId, [.. idRanges]);
            await Host.Services.Queues().Enqueue(summarize, cancellationToken).ConfigureAwait(false);
        }

        // If we reached the end of the entries, we might want to summarize the last group
        if (!hasMore) {
            var lastEntry = state.LastEntry;
            if (lastEntry != null && (lastEntry.EndsAt ?? lastEntry.BeginsAt) < Clocks.CoarseSystemClock.Now - Settings.ChatEntrySummarizationDelay)
                if (state.WordCount >= Settings.MinConversationWords && state.EntryCount >= Settings.MinConversationEntries) {
                    var group = state.CurrentGroup!.AddRange(state.CurrentChunk?.Entries ?? []).Build();

                    var idRanges = group.LocalIdRanges;
                    var summarize = new ConversationBackend_Summarize(chatId, [.. idRanges]);
                    await Host.Services.Queues().Enqueue(summarize, cancellationToken).ConfigureAwait(false);

                    // Reset the state for the next group
                    ExtractorState = new ExtractorState(null, null);
                }
            if (entries.Count > 0)
                Cursor = entries[^1].LocalId;
            Event.MarkHandled();
            return WaitForEvent(FlowSteps.OnIndex, InfiniteHardResumeAt);
        }

        if (entries.Count > 0)
            Cursor = entries[^1].LocalId;
        Event.MarkHandled();
        return hasMore
            ? StoreAndResume(FlowSteps.OnIndex, "Continue processing next batch")
            : WaitForEvent(FlowSteps.OnIndex, InfiniteHardResumeAt);
    }

    // Private methods

    private async Task<(IReadOnlyList<TextEntry> Entries, bool HasMore)> GetEntries(
        long lastId,
        CancellationToken cancellationToken)
    {
        var chatId = ChatId;
        var now = Clocks.CoarseSystemClock.Now;
        var immatureMoment = now - Settings.ChatEntrySummarizationDelay;
        var entries = await ChatsBackend.ListNewEntries(chatId, lastId, BatchSize + 1, cancellationToken).ConfigureAwait(false);
        var textEntries = entries
            .TakeWhile(e => (e.EndsAt ?? e.BeginsAt) <= immatureMoment)
            .Select(e => new TextEntry(e))
            .Take(BatchSize)
            .ToList();
        var hasMore = entries.Length > BatchSize;
        return (textEntries, hasMore);
    }
}
