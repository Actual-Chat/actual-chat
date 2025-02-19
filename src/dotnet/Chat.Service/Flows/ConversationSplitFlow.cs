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
        if (entryBeginsAt > immatureMoment)
            batch = batch.TakeWhile(e => e.BeginsAt <= immatureMoment).ToList();

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

            var tileRange = IdTileStack.FirstLayer.GetTile(entryLid).Range;
            var existingConversations = await ConversationsBackend.List(chatId, tileRange, cancellationToken).ConfigureAwait(false);
            var appendReply = new ConversationBackend_AppendReply(
                existingConversations.Count == 0 ? ConversationId.None : existingConversations[0],
                entryLid,
                [..replySequence.Entries]
            ) {
                DelayUntil = existingConversations.Count == 0
                    ? Host.Clocks.CoarseSystemClock.Now + Settings.ChatEntrySummarizationDelay
                    : null,
            };
            await Host.Services.Queues().Enqueue(appendReply, cancellationToken).ConfigureAwait(false);
        }

        if (groups.Count == 0)
            return;

        foreach (var group in groups) {
            var summarize = new ConversationBackend_Summarize(chatId, [..group.Entries]) {
                DelayUntil = Host.Clocks.CoarseSystemClock.Now + Settings.ChatEntrySummarizationDelay,
            };
            await Host.Services.Queues().Enqueue(summarize, cancellationToken).ConfigureAwait(false);
        }
    }
}
