using ActualChat.Chat.ML;
using ActualChat.Chat.Module;
using ActualChat.Flows;
using ActualChat.Queues;
using MemoryPack;

namespace ActualChat.Chat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class ConversationSplitFlow: BatchedIndexingFlowBase<ChatEntry, ChatEntryId>
{
    private static readonly TileStack<long> IdTileStack = Constants.Chat.ServerIdTileStack;
    protected override int CurrentFlowSetVersion => 1;
    private ChatId ChatId => field != ChatId.None ? field : field = new ChatId(Id.Arguments, ParseOrNone.Option);

    [field: AllowNull, MaybeNull]
    private ChatSettings Settings => field ??= Host.Services.GetRequiredService<ChatSettings>();

    [field: AllowNull, MaybeNull]
    private IChatsBackend ChatsBackend => field ??= Host.Services.GetRequiredService<IChatsBackend>();

    [field: AllowNull, MaybeNull]
    private IConversationsBackend ConversationsBackend => field ??= Host.Services.GetRequiredService<IConversationsBackend>();

    [field: AllowNull, MaybeNull]
    private IEntryGroupExtractor EntryGroupExtractor => field ??= Host.Services.GetRequiredKeyedService<IEntryGroupExtractor>(EntryGroupLimit.None);

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public ExtractorState? ExtractorState { get; protected set; }

    protected override void ResetState()
    {
        base.ResetState();
        ExtractorState = null;
    }

    protected override async Task<bool> OnBeforeFirstIndexAfterReset(CancellationToken cancellationToken)
    {
        var chat = await ChatsBackend.Get(ChatId,  cancellationToken).ConfigureAwait(false);
        if (chat!.IsSummarized ?? false) // Only process summarized chats
            return await base.OnBeforeFirstIndexAfterReset(cancellationToken);

        return false;
    }

    protected override async Task<IReadOnlyList<ChatEntry>> GetBatch(
        IndexingFlowCursor<ChatEntryId>? cursor,
        CancellationToken cancellationToken)
    {
        var chatId = ChatId;
        cursor ??= new (new ChatEntryId(chatId, ChatEntryKind.Text, 0, AssumeValid.Option), 0);
        IReadOnlyList<ChatEntry> batch = await ChatsBackend
            .ListNewEntries(chatId, cursor.LastUpdatedId.LocalId, BatchSize, cancellationToken)
            .ConfigureAwait(false);
        if (batch.Count == 0)
            return batch;

        var now = Clocks.CoarseSystemClock.Now;
        var immatureMoment = now - Settings.ChatEntrySummarizationDelay;
        var last = batch[^1];
        var entryBeginsAt = last.BeginsAt;
        if (entryBeginsAt > immatureMoment) {
            batch = batch.TakeWhile(e => e.BeginsAt <= immatureMoment).ToList();
            await Host.Flows.GetAndResume<ConversationSplitFlow>(ChatId,
                    "ScheduleSummarize",
                    entryBeginsAt + Settings.ChatEntrySummarizationDelay,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        Log.LogDebug("`{Id}`.GetBatch: retrieved {Count} items with cursor={Cursor}", Id, batch.Count,cursor);
        return batch;
    }

    protected override async Task ProcessBatch(IReadOnlyList<ChatEntry> batch, CancellationToken cancellationToken)
    {
        var state = ExtractorState;
        var chatId = ChatId;
        var entries = batch.Select(c => new TextEntry(c)).ToList();
        var extractResult = await EntryGroupExtractor.ExtractGroups(state, entries, cancellationToken).ConfigureAwait(false);
        ExtractorState = extractResult.State;

        var groups = extractResult.Groups;
        var replySequences = extractResult.ReplySequences;
        foreach (var replySequence in replySequences) {
            var firstEntry = replySequence.Entries[0];
            if (firstEntry.RepliedEntryLid is not {} entryLid)
                continue; // First entry in the sequence is not a reply!

            var lastEntry = replySequence.Entries[^1];
            var idTileRange = IdTileStack.FirstLayer.GetTile(entryLid).Range;
            var existingConversations = await ConversationsBackend.List(chatId, idTileRange, cancellationToken).ConfigureAwait(false);

            var appendReply = new ConversationBackend_AppendReply(
                chatId,
                entryLid,
                new Range<long>(firstEntry.LocalId, lastEntry.LocalId + 1)
            ) {
                DelayUntil = existingConversations.Count == 0
                    ? Host.Clocks.CoarseSystemClock.Now + (2 * Settings.ChatEntrySummarizationDelay)
                    : null,
            };
            await Host.Services.Queues().Enqueue(appendReply, cancellationToken).ConfigureAwait(false);
        }

        if (groups.Count == 0)
            return;

        foreach (var group in groups) {
            if (group.WordCount < Settings.MinConversationWords)
                continue;
            if (group.Entries.Count < Settings.MinConversationEntries)
                continue;

            var idRanges = new List<Range<long>>();
            long? startId = null, endId = null;

            foreach (var entry in group.Entries)
                if (startId == null) {
                    startId = entry.LocalId;
                    endId = entry.LocalId;
                } else if (entry.LocalId == endId + 1)
                    endId = entry.LocalId;
                else {
                    idRanges.Add(new Range<long>(startId.Value, endId!.Value + 1));
                    startId = entry.LocalId;
                    endId = entry.LocalId;
                }

            if (startId != null && endId != null)
                idRanges.Add(new Range<long>(startId.Value, endId.Value + 1));
            if (idRanges.Count == 0)
                continue; // No valid ranges - we should not get there

            var summarize = new ConversationBackend_Summarize(chatId, [..idRanges]) {
                DelayUntil = Host.Clocks.CoarseSystemClock.Now + Settings.ChatEntrySummarizationDelay,
            };
            await Host.Services.Queues().Enqueue(summarize, cancellationToken).ConfigureAwait(false);
        }
    }
}
