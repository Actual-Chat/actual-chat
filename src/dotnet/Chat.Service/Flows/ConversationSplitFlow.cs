using ActualChat.Chat.ML;
using ActualChat.Chat.Module;
using ActualChat.Flows;
using ActualChat.Queues;
using ActualChat.Streaming;

namespace ActualChat.Chat.Flows;

[Flow(ResumeTimeout = 60)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial class ConversationSplitFlow : Flow<Unit>, IHasLastRunAt
{
    private const int BatchSize = 100;
    private static readonly TimeSpan MaxDelay = TimeSpan.FromDays(7);
    private static readonly TileStack<long> IdTileStack = Constants.Chat.ServerIdTileStack;

    private ChatSettings Settings => field ??= Services.GetRequiredService<ChatSettings>();
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private IConversationsBackend ConversationsBackend => field ??= Services.GetRequiredService<IConversationsBackend>();
    private ILiveSessionsBackend LiveSessionsBackend => field ??= Services.GetRequiredService<ILiveSessionsBackend>();
    private IEntryGroupExtractor EntryGroupExtractor => field ??= Services.GetRequiredKeyedService<IEntryGroupExtractor>(EntryGroupLimit.None);

    private ChatId ChatId => field ??= ChatId.Parse(Id.Arguments);

    [DataMember(Order = 0), MemoryPackOrder(0), Key(0)]
    public ExtractorState? ExtractorState { get; set; }
    [DataMember(Order = 1), MemoryPackOrder(1), Key(1)]
    public long LastLid { get; set; }
    [DataMember(Order = 2), MemoryPackOrder(2), Key(2)]
    public Moment LastRunAt { get; set; }
    [DataMember(Order = 3), MemoryPackOrder(3), Key(3)]
    public Moment LastSummaryAt { get; set; }
    [DataMember(Order = 4), MemoryPackOrder(4), Key(4)]
    public Range<long>[] LastSummaryLidRanges { get; set; } = [];
    [DataMember(Order = 5), MemoryPackOrder(5), Key(5)]
    public FlowReadiness LastReadiness { get; set; }

    private async ValueTask<FlowReadiness> Prepare(CancellationToken cancellationToken)
    {
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
            Console.Log($"Prepare() -> {readiness}, will resume at {resumeDelay}");
            Runtime.StageResumeAt(resumeDelay);
            return;
        }

        await Process(cancellationToken).ConfigureAwait(false);
    }

    private async Task Process(CancellationToken cancellationToken)
    {
        var now = ResumedAt;
        LastRunAt = now;
        Console.Log($"Process: LastLid={LastLid}");

        // Never (re)summarize a range a latched live session owns — the live flow materializes it.
        var live = await LiveSessionsBackend.GetState(ChatId, cancellationToken).ConfigureAwait(false);
        var liveSessionRange = live is { SessionStartedAt: not null } lc
            ? new Range<long>(lc.ContextStartLid > 0 ? lc.ContextStartLid : lc.StartEntryLid, long.MaxValue)
            : (Range<long>?)null;
        bool OverlapsLiveSession(IReadOnlyList<Range<long>> ranges)
            => liveSessionRange is { } lsr && ranges.Any(r => !r.IntersectWith(lsr).IsEmpty);

        var (entries, hasMore, hasImmature) = await GetEntries(LastLid, cancellationToken).ConfigureAwait(false);
        Console.Log($"Fetched {entries.Count} entries, hasMore={hasMore}, hasImmature={hasImmature}");
        var (state, groups, replySequences) = EntryGroupExtractor.ExtractGroups(ExtractorState ?? new ExtractorState(null, null), entries);
        ExtractorState = state;
        Console.Log($"Extracted {groups.Count} groups, {replySequences.Count} replies, state: {state.EntryCount} entries, {state.WordCount} words");
        var hasEntries = entries.Count > 0;

        // Process replies
        foreach (var replySequence in replySequences) {
            var firstEntry = replySequence.Entries[0];
            if (firstEntry.RepliedEntryLid is not { } entryLid)
                continue; // The first entry in the sequence must be not a reply!

            var lastEntry = replySequence.Entries[^1];
            var replyRange = new Range<long>(firstEntry.LocalId, lastEntry.LocalId + 1);
            if (OverlapsLiveSession([replyRange]))
                continue; // The live flow owns its range; don't append replies into it.

            var idRange = IdTileStack.LastLayer.GetTile(entryLid).Range;
            var rangeMeta = await ConversationsBackend.GetRangeMeta(ChatId, idRange.Start, cancellationToken).ConfigureAwait(false);
            var existingConversationIds = rangeMeta.ConversationIds;
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

            Console.Log($"No groups yet, continuing from {LastLid}");
            // Continue immediately to process the next batch
            Runtime.StageResume();
            return;
        }

        foreach (var group in groups) {
            if (group.WordCount < Settings.Summarization.MinConversationWords) {
                Console.Log($"Skipping group: {group.WordCount} words < {Settings.Summarization.MinConversationWords} min");
                continue;
            }

            if (group.Entries.Count < Settings.Summarization.MinConversationEntries) {
                Console.Log($"Skipping group: {group.Entries.Count} entries < {Settings.Summarization.MinConversationEntries} min");
                continue;
            }

            var idRanges = group.LocalIdRanges;
            if (LastSummaryLidRanges.SequenceEqual(idRanges)) {
                Console.Log("Skipping group: ranges match LastSummaryRanges");
                continue;
            }

            if (await IsAlreadyCovered(idRanges, cancellationToken).ConfigureAwait(false)) {
                Console.Log("Skipping group: already covered by an existing conversation (materialized live conversation)");
                continue;
            }

            if (OverlapsLiveSession(idRanges)) {
                Console.Log("Skipping group: overlaps an active live session");
                continue;
            }

            Console.Log($"Enqueuing summarize for group: {group.Entries.Count} entries, {group.WordCount} words");
            var summarize = new ConversationBackend_Summarize(ChatId, [.. idRanges]);
            await Services.Queues().Enqueue(summarize, cancellationToken).ConfigureAwait(false);
            LastSummaryAt = now;
            LastSummaryLidRanges = idRanges.ToArray();
        }

        if (hasEntries)
            LastLid = entries[^1].LocalId;

        // If we reached the end of the entries, we might want to summarize the last group
        if (!hasMore) {
            var lastRanges = LastSummaryLidRanges;
            var currentRanges = new EntryGroupBuilder(state.CurrentGroup)
                .AddRange(state.CurrentChunk?.Entries ?? [])
                .Build().LocalIdRanges;
            var rangesAreEqual = lastRanges.SequenceEqual(currentRanges);
            var hasCurrentRanges = currentRanges.Count > 0;

            var tooOften = hasImmature && (LastSummaryAt + Settings.Summarization.ResummarizationDelay >= now);
            var readyToSummarize =
                state.CurrentGroup != null
                && state.WordCount >= Settings.Summarization.MinConversationWords
                && state.EntryCount >= Settings.Summarization.MinConversationEntries
                && !rangesAreEqual;
            Console.Log($"End-of-stream: readyToSummarize={readyToSummarize}, tooOften={tooOften}, rangesAreEqual={rangesAreEqual}, hasCurrentRanges={hasCurrentRanges}");

            if (readyToSummarize && !tooOften) {
                // Summarize the current group
                var groupBuilder = state.CurrentGroup!.AddRange(state.CurrentChunk?.Entries ?? []);
                var group = groupBuilder.Build();

                var idRanges = group.LocalIdRanges;
                if (!LastSummaryLidRanges.SequenceEqual(idRanges) && !OverlapsLiveSession(idRanges)) {
                    Console.Log($"Enqueuing end-of-stream summarize: {group.Entries.Count} entries, {group.WordCount} words");
                    var summarize = new ConversationBackend_Summarize(ChatId, [.. idRanges]);
                    await Services.Queues().Enqueue(summarize, cancellationToken).ConfigureAwait(false);
                    LastSummaryAt = now;
                    LastSummaryLidRanges = idRanges.ToArray();
                }

                // Keep the group if there are immature items, otherwise clear the chunk
                ExtractorState = new ExtractorState(
                    hasImmature ? groupBuilder : new EntryGroupBuilder(),
                    new EntryGroupBuilder()
                );
            }

            // Schedule next resume
            if (hasImmature || (tooOften && !rangesAreEqual && hasCurrentRanges)) {
                var resumeDelay = hasImmature && !tooOften
                    ? Settings.Summarization.ChatEntrySummarizationDelay
                    : Settings.Summarization.ResummarizationDelay;
                Console.Log($"Scheduling delayed resume: hasImmature={hasImmature}, tooOften={tooOften}, delay={resumeDelay}");
                Runtime.StageResumeIn(resumeDelay);
            }
            else
                Console.Log("End-of-stream: completing (no resume needed)");
            return;
        }

        if (hasMore) {
            Console.Log("Resuming immediately: hasMore=true");
            Runtime.StageResume(); // Continue immediately
        }
        else if (hasImmature) {
            Console.Log("Scheduling delayed resume: hasImmature=true");
            Runtime.StageResumeIn(Settings.Summarization.ChatEntrySummarizationDelay);
        }
        else
            Console.Log("Completing: no more entries");
    }

    // Private methods

    private async Task<(IReadOnlyList<ChatEntrySlim> Entries, bool HasMore, bool HasImmatureInWindow)> GetEntries(
        long lastId,
        CancellationToken cancellationToken)
    {
        var now = ResumedAt;
        var chatId = ChatId;
        var immatureMoment = now - Settings.Summarization.ChatEntrySummarizationDelay;

        // A latched live session owns its tail [StartEntryLid, ...); pre-latch (solo) the split flow summarizes normally.
        var live = await LiveSessionsBackend.GetState(chatId, cancellationToken).ConfigureAwait(false);
        var liveStartLid = live is { SessionStartedAt: not null } lc
            ? (lc.ContextStartLid > 0 ? lc.ContextStartLid : lc.StartEntryLid)
            : long.MaxValue;

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

        // Detect the first entry that falls into the active live conversation range
        var firstLiveIndex = -1;
        for (var i = 0; i < entries.Length; i++)
            if (entries[i].LocalId >= liveStartLid) {
                firstLiveIndex = i;
                break;
            }

        var availableCount = entries.Length;
        if (firstImmatureIndex >= 0)
            availableCount = Math.Min(availableCount, firstImmatureIndex);
        if (firstLiveIndex >= 0)
            availableCount = Math.Min(availableCount, firstLiveIndex);

        var take = Math.Min(availableCount, BatchSize);
        var textEntries = entries
            .Take(take)
            .Select(e => new ChatEntrySlim(e))
            .ToList();

        var hasMore = entries.Length > BatchSize && availableCount > BatchSize;
        var hasImmature = firstImmatureIndex >= 0 || firstLiveIndex >= 0;

        return (textEntries, hasMore, hasImmature);
    }

    private async Task<bool> IsAlreadyCovered(IReadOnlyList<Range<long>> idRanges, CancellationToken cancellationToken)
    {
        var conversationId = ConversationId.New(ChatId, idRanges[0].Start);
        var existing = await ConversationsBackend.Get(conversationId, cancellationToken).ConfigureAwait(false);
        return existing != null && existing.EndEntryLid >= idRanges[^1].End - 1;
    }
}
