using ActualChat.Chat.ML;
using ActualChat.Chat.Module;
using ActualChat.Flows;
using MemoryPack;

namespace ActualChat.Chat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class ConversationSplitFlow: BatchedIndexingFlowBase<ChatEntry, ChatEntryId>
{
    protected override int CurrentFlowSetVersion => 1;

    [field: AllowNull, MaybeNull]
    private ChatSettings Settings => field ??= Host.Services.GetRequiredService<ChatSettings>();

    [field: AllowNull, MaybeNull]
    private IChatsBackend ChatsBackend => field ??= Host.Services.GetRequiredService<IChatsBackend>();

    [field: AllowNull, MaybeNull]
    private IEntryGroupExtractor EntryGroupExtractor => field ??= Host.Services.GetRequiredKeyedService<IEntryGroupExtractor>(EntryGroupLimit.None);

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public ExtractorState? ExtractorState { get; protected set; }

    protected override async Task<IReadOnlyList<ChatEntry>> GetBatch(
        IndexingFlowCursor<ChatEntryId>? cursor,
        CancellationToken cancellationToken)
    {
        var chatId = new ChatId(Id.Arguments);
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
        var extractResult = await EntryGroupExtractor.ExtractGroups(state, batch, cancellationToken).ConfigureAwait(false);
        ExtractorState = extractResult.State;

        var groups = extractResult.Groups;
        var replySequences = extractResult.ReplySequences;
        if (groups.Count == 0)
            return;

        // TODO(AK): Implement the logic to split the conversation into documents
    }
}
