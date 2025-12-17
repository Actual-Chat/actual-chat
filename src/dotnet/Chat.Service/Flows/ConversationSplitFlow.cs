using ActualChat.Chat.ML;
using ActualChat.Chat.Module;
using ActualChat.Flows;
using ActualChat.Queues;
using MemoryPack;

namespace ActualChat.Chat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class ConversationSplitFlow : Flow<Unit>, IHasLastRunAt
{
    private const int BatchSize = 100;
    private TimeSpan MaxDelay => TimeSpan.FromDays(7);
    private static readonly TileStack<long> IdTileStack = Constants.Chat.ServerIdTileStack;
    private ChatId ChatId { get; set; } = null!;

    private ChatSettings Settings => field ??= Services.GetRequiredService<ChatSettings>();
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private IConversationsBackend ConversationsBackend => field ??= Services.GetRequiredService<IConversationsBackend>();
    private IEntryGroupExtractor EntryGroupExtractor => field ??= Services.GetRequiredKeyedService<IEntryGroupExtractor>(EntryGroupLimit.None);

    // Flow state

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public ExtractorState? ExtractorState { get; private set; }
    [DataMember(Order = 1), MemoryPackOrder(1)]
    public long LastLid { get; private set; }
    [DataMember(Order = 2), MemoryPackOrder(2)]
    public Moment LastRunAt { get; private set; }
    [DataMember(Order = 3), MemoryPackOrder(3)]
    public Moment LastSummaryAt { get; private set; }
    [DataMember(Order = 4), MemoryPackOrder(4)]
    public Range<long>[] LastSummaryRanges { get; private set; } = [];
    [DataMember(Order = 5), MemoryPackOrder(5)]
    public FlowReadiness LastReadiness { get; private set; }

    protected override ValueTask Init(CancellationToken cancellationToken)
        => default;

    private async ValueTask<FlowReadiness> Prepare(CancellationToken cancellationToken)
    {
        ChatId = ChatId.Parse(Id.Arguments);
        var chat = await ChatsBackend.Get(ChatId, cancellationToken).ConfigureAwait(false);
        if (chat is null) {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            chat = await ChatsBackend.Get(ChatId, cancellationToken).ConfigureAwait(false);
        }
        if (chat is null)
            return "Chat does not exist";
        if (!(chat.IsSummarized ?? false))
            return "Chat doesn't have summarization enabled";

        return FlowReadiness.Ready;
    }

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        LastReadiness = await Prepare(cancellationToken).ConfigureAwait(false);
        if (LastReadiness is { IsSuspended: true } readiness) {
            var resumeDelay = ResumedAt + (readiness.ResumeDelay ?? MaxDelay);
            Console.Log($"Prepare -> {readiness}, will resume at {resumeDelay}");
            Runtime.StageResumeAt(resumeDelay);
            return;
        }

        await Process(cancellationToken).ConfigureAwait(false);
    }

    private async Task Process(CancellationToken cancellationToken)
    {
        var now = ResumedAt;
        LastRunAt = now;
        Console.Log($"Process: started at {LastLid}");

        var (entries, hasMore, hasImmature) = await GetEntries(LastLid, cancellationToken).ConfigureAwait(false);
        var (state, groups, replySequences) = EntryGroupExtractor.ExtractGroups(ExtractorState ?? new ExtractorState(null, null), entries);
        ExtractorState = state;
        var hasEntries = entries.Count > 0;

        // Process replies
        foreach (var replySequence in replySequences) {
            var firstEntry = replySequence.Entries[0];
            if (firstEntry.RepliedEntryLid is not { } entryLid)
                continue; // The first entry in the sequence must be not a reply!

            var lastEntry = replySequence.Entries[^1];
            var idTileRange = IdTileStack.LastLayer.GetTile(entryLid).Range;
            var conversationTile = await ConversationsBackend.GetRangeMeta(ChatId, idTileRange.Start, cancellationToken).ConfigureAwait(false);
            var existingConversationIds = conversationTile.ConversationIds;
            var appendReply = new ConversationBackend_AppendReply(
                ChatId,
                entryLid,
                new Range<long>(firstEntry.LocalId, lastEntry.LocalId + 1)
            ) {
                DelayUntil = existingConversationIds.Length == 0
                    ? now + (2 * Settings.Summarization.ChatEntrySummarizationDelay)
                    : default,
            };
            await Services.Queues().Enqueue(appendReply, cancellationToken).ConfigureAwait(false);
        }

        if (groups.Count == 0 && hasMore) {
            // No groups found, but we have more entries to process
            if (entries.Count > 0)
                LastLid = entries[^1].LocalId;

            // Continue immediately to process the next batch
            Runtime.StageResume();
            return;
        }

        foreach (var group in groups) {
            if (group.WordCount < Settings.Summarization.MinConversationWords)
                continue;

            if (group.Entries.Count < Settings.Summarization.MinConversationEntries)
                continue;

            var idRanges = group.LocalIdRanges;
            var summarize = new ConversationBackend_Summarize(ChatId, [.. idRanges]);
            await Services.Queues().Enqueue(summarize, cancellationToken).ConfigureAwait(false);
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

            var tooOften = LastSummaryAt + Settings.Summarization.ChatEntrySummarizationDelay >= now;
            var readyToSummarize =
                state.CurrentGroup != null
                && state.WordCount >= Settings.Summarization.MinConversationWords
                && state.EntryCount >= Settings.Summarization.MinConversationEntries
                && !rangesAreEqual;

            if (hasEntries)
                LastLid = entries[^1].LocalId;

            if (readyToSummarize && !tooOften) {
                // Summarize the current group
                var groupBuilder = state.CurrentGroup!.AddRange(state.CurrentChunk?.Entries ?? []);
                var group = groupBuilder.Build();

                var idRanges = group.LocalIdRanges;
                var summarize = new ConversationBackend_Summarize(ChatId, [.. idRanges]);
                await Services.Queues().Enqueue(summarize, cancellationToken).ConfigureAwait(false);

                // Keep the group if there are immature items, otherwise clear the chunk
                ExtractorState = new ExtractorState(
                    hasImmature ? groupBuilder : new EntryGroupBuilder(),
                    new EntryGroupBuilder()
                );
                LastSummaryAt = now;
                LastSummaryRanges = idRanges.ToArray();
            }

            // Schedule next resume
            if (hasImmature || (tooOften && !rangesAreEqual && hasCurrentRanges))
                Runtime.StageResumeIn(Settings.Summarization.ChatEntrySummarizationDelay);
            return;
        }

        if (hasEntries)
            LastLid = entries[^1].LocalId;

        if (hasMore)
            Runtime.StageResume(); // Continue immediately
        else if (hasImmature)
            Runtime.StageResumeIn(Settings.Summarization.ChatEntrySummarizationDelay);
    }

    // Private methods

    private async Task<(IReadOnlyList<TextEntry> Entries, bool HasMore, bool HasImmatureInWindow)> GetEntries(
        long lastId,
        CancellationToken cancellationToken)
    {
        var now = ResumedAt;
        var chatId = ChatId;
        var immatureMoment = now - Settings.Summarization.ChatEntrySummarizationDelay;

        // Fetch up to (BatchSize + 1) items
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
