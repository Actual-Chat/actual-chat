using ActualChat.Chat.ML;
using ActualChat.Chat.Module;
using ActualChat.Flows;
using ActualChat.Queues;
using MemoryPack;

namespace ActualChat.Chat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class ConversationSplitFlow : IndexingFlowBase<long>
{
    private const int BatchSize = 100;
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
        var chat = await ChatsBackend.Get(ChatId, cancellationToken).Require().ConfigureAwait(false);
        if (chat.IsSummarized ?? false) // Only process summarized chats
            return await base.OnBeforeFirstIndexAfterReset(cancellationToken).ConfigureAwait(false);

        return false;
    }

    protected override async Task<BatchIndexingResult<long>> Process(
        long previousLastLid,
        CancellationToken cancellationToken)
    {
        var batch = await GetEntries(previousLastLid, cancellationToken).ConfigureAwait(false);
        DebugLog?.LogDebug("`{Id}`.Process: retrieved {Count} entries with localId > {PreviousLastLid}", Id, batch.Count, previousLastLid);

        var state = ExtractorState;
        var chatId = ChatId;
        var entries = batch.Select(c => new TextEntry(c)).ToList();
        var (extractorState, groups, replySequences) = EntryGroupExtractor.ExtractGroups(state, entries);
        ExtractorState = extractorState;

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
                    : null,
            };
            await Host.Services.Queues().Enqueue(appendReply, cancellationToken).ConfigureAwait(false);
        }

        var isTailReached = batch.Count < BatchSize;
        if (groups.Count == 0)
            return new (false, isTailReached, ExtractorState.MaxLid, false);

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
                }
                else if (entry.LocalId == endId + 1)
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

            var summarize = new ConversationBackend_Summarize(chatId, [.. idRanges]) {
                DelayUntil = Host.Clocks.CoarseSystemClock.Now + Settings.ChatEntrySummarizationDelay,
            };
            await Host.Services.Queues().Enqueue(summarize, cancellationToken).ConfigureAwait(false);
        }

        return new(false, isTailReached, entries[^1].LocalId, true);
    }

    private async Task<IReadOnlyList<ChatEntry>> GetEntries(
        long lastId,
        CancellationToken cancellationToken)
    {
        var chatId = ChatId;
        var now = Clocks.CoarseSystemClock.Now;
        var immatureMoment = now - Settings.ChatEntrySummarizationDelay;
        var entries = await ChatsBackend.ListNewEntries(chatId, lastId, BatchSize, cancellationToken).ConfigureAwait(false);
        // TODO(AK): probably filtering by time must be done in Backend
        return [.. entries.TakeWhile(e => (e.EndsAt ?? e.BeginsAt) <= immatureMoment)];
    }
}
