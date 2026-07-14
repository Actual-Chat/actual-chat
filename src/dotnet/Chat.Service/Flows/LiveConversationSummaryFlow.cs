using ActualChat.Chat.ML;
using ActualChat.Chat.Module;
using ActualChat.Flows;
using ActualChat.Live;
using ActualChat.Streaming;

namespace ActualChat.Chat.Flows;

/// <summary>
/// Throttled resummarization of an in-progress live conversation, plus close-time finalization: once the
/// backend marks a latched transcription session <c>IsClosing</c>, this flow runs the final summary pass,
/// decides the tier, and materializes (or vanishes) the conversation before calling
/// <c>ILiveSessionsBackend.FinalizeSession</c>.
/// </summary>
[Flow(ResumeTimeout = 60, DelayQuanta = 15)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial class LiveConversationSummaryFlow : Flow<Unit>
{
    private const int MaxEntries = 1000;
    private const int ContextScanBudget = 200;
    private static readonly TileStack<long> IdTileStack = Constants.Chat.ServerIdTileStack;
    // Resume throttle: the flow re-checks this often so entries maturing during silence (and the close/finalize
    // once nobody is talking) get handled without a new audio entry to trigger it; resummary itself is
    // additionally gated on Settings.Summarization.ResummarizationDelay. Keep DelayQuanta (see [Flow]) <= Throttle
    // so overlapping self-resumes coalesce without the loop stalling.
    private static readonly TimeSpan Throttle = TimeSpan.FromSeconds(15);

    private ChatSettings Settings => field ??= Services.GetRequiredService<ChatSettings>();
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private IConversationsBackend ConversationsBackend => field ??= Services.GetRequiredService<IConversationsBackend>();
    private IConversationSummarizer ConversationSummarizer => field ??= Services.GetRequiredService<IConversationSummarizer>();
    private ILiveSessionsBackend LiveSessionsBackend => field ??= Services.GetRequiredService<ILiveSessionsBackend>();

    private ChatId ChatId => field ??= ChatId.Parse(Id.Arguments);

    [DataMember(Order = 0), MemoryPackOrder(0), Key(0)]
    public long LastSummaryEndLid { get; set; }

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        var live = await LiveSessionsBackend.GetState(ChatId, cancellationToken).ConfigureAwait(false);
        if (live is null)
            return; // closed

        if (live.IsClosing) {
            // A latched transcription session hands its close to this flow; other shapes are the backend's.
            if (live is { TranscriptionOn: true, SessionStartedAt: not null, Kind: not LiveSessionKind.Call })
                await Finalize(live, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Pre-latch (solo): the normal ConversationSplitFlow owns summarization, so don't double-summarize.
        if (live.SessionStartedAt is null) {
            Runtime.StageResumeIn(Throttle);
            return;
        }

        var contextStartLid = await EnsureContextStart(live, cancellationToken).ConfigureAwait(false);

        var neverSummarized = LastSummaryEndLid == 0;
        // The first summary lands early (low gates, short maturity) so short calls still get a title; re-summaries
        // keep the higher split-flow gates and cadence.
        var maturity = neverSummarized
            ? Settings.Summarization.FirstLiveSummaryDelay
            : Settings.Summarization.ChatEntrySummarizationDelay;
        var minEntries = neverSummarized
            ? Settings.Summarization.MinLiveConversationEntries
            : Settings.Summarization.MinConversationEntries;
        var minWords = neverSummarized
            ? Settings.Summarization.MinLiveConversationWords
            : Settings.Summarization.MinConversationWords;

        var entries = await GetEntries(contextStartLid, maturity, cancellationToken).ConfigureAwait(false);
        var enoughData = entries.Count >= minEntries && entries.Sum(WordCount) >= minWords;
        var hasNew = entries.Count > 0 && entries[^1].LocalId > LastSummaryEndLid;
        var dueForResummary = ResumedAt - live.LastSummaryAt >= Settings.Summarization.ResummarizationDelay;
        if (enoughData && hasNew && (neverSummarized || dueForResummary)) {
            var result = await ConversationSummarizer.Summarize(entries, cancellationToken).ConfigureAwait(false);
            if (result.Summary is { } summary) {
                await LiveSessionsBackend
                    .UpdateSummary(ChatId, ToLiveSummary(summary, entries), cancellationToken)
                    .ConfigureAwait(false);
                LastSummaryEndLid = entries[^1].LocalId;
            }
        }

        Runtime.StageResumeIn(Throttle);
    }

    // Private methods

    private async Task Finalize(LiveSessionState live, CancellationToken cancellationToken)
    {
        var contextStartLid = await EnsureContextStart(live, cancellationToken).ConfigureAwait(false);
        var entries = await GetEntries(contextStartLid, maturity: null, cancellationToken).ConfigureAwait(false);
        var words = entries.Sum(WordCount);

        // Tier 1: below the first-summary floor - nothing worth keeping; FinalizeSession vanishes it (empty title).
        if (entries.Count < Settings.Summarization.MinLiveConversationEntries
            || words < Settings.Summarization.MinLiveConversationWords) {
            await LiveSessionsBackend.FinalizeSession(ChatId, cancellationToken).ConfigureAwait(false);
            Runtime.StageResumeIn(Throttle);
            return;
        }

        var result = await ConversationSummarizer.Summarize(entries, cancellationToken).ConfigureAwait(false);
        if (result.Summary is not { } summary) {
            Runtime.StageResumeIn(TimeSpan.FromSeconds(5)); // retry within the backend's 90s SelfClose backstop
            return;
        }

        // Tier 2 (below the full-summary gate) materializes expanded; tier 3 (at/above it) materializes collapsed.
        var isExpandedByDefault = words < Settings.Summarization.MinConversationWords
            || entries.Count < Settings.Summarization.MinConversationEntries;
        await LiveSessionsBackend
            .UpdateSummary(ChatId, ToLiveSummary(summary, entries, isExpandedByDefault), cancellationToken)
            .ConfigureAwait(false);
        await LiveSessionsBackend.FinalizeSession(ChatId, cancellationToken).ConfigureAwait(false);
        Runtime.StageResumeIn(Throttle);
    }

    private async Task<long> EnsureContextStart(LiveSessionState live, CancellationToken cancellationToken)
    {
        if (live.ContextStartLid > 0)
            return live.ContextStartLid;
        if (!live.TranscriptionOn)
            return live.StartEntryLid;

        var anchorLid = live.StartEntryLid;
        var minLid = (await ChatsBackend.GetLidRange(ChatId, false, cancellationToken).ConfigureAwait(false)).Start;
        var (anchor, preceding) = await GetContextScanEntries(anchorLid, minLid, cancellationToken).ConfigureAwait(false);
        if (anchor is null)
            return anchorLid;

        var contextStart = ContextStartScanner.FindContextStartLid(preceding, anchor);

        // Never re-claim a persisted conversation's range: clamp to just past the one preceding the scan start.
        var idRange = IdTileStack.LastLayer.GetTile(contextStart).Range;
        var rangeMeta = await ConversationsBackend.GetRangeMeta(ChatId, idRange.Start, cancellationToken).ConfigureAwait(false);
        if (rangeMeta.PreviousConversationLidRange is { } prev && prev.End > contextStart)
            contextStart = prev.End;

        await LiveSessionsBackend.SetContextStart(ChatId, contextStart, cancellationToken).ConfigureAwait(false);
        return contextStart;
    }

    private async Task<(ChatEntrySlim? Anchor, IReadOnlyList<ChatEntrySlim> Preceding)> GetContextScanEntries(
        long anchorLid, long minLid, CancellationToken cancellationToken)
    {
        ChatEntrySlim? anchor = null;
        var preceding = new List<ChatEntrySlim>();
        var tile = IdTileStack.LastLayer.GetTile(anchorLid);
        while (preceding.Count < ContextScanBudget) {
            var chatTile = await ChatsBackend.GetTile(ChatId, tile.Range, false, cancellationToken).ConfigureAwait(false);
            foreach (var entry in chatTile.Entries) {
                if (entry.Content.IsNullOrEmpty())
                    continue;

                var slim = new ChatEntrySlim(entry);
                if (slim.LocalId == anchorLid)
                    anchor ??= slim;
                else if (slim.LocalId < anchorLid)
                    preceding.Add(slim);
            }
            if (tile.Range.Start <= minLid)
                break;

            tile = IdTileStack.LastLayer.GetTile(tile.Range.Start - 1);
        }
        preceding.Sort(ChatEntrySlim.LocalIdComparer);
        if (preceding.Count > ContextScanBudget)
            preceding = preceding.Skip(preceding.Count - ContextScanBudget).ToList();

        return (anchor, preceding);
    }

    private async Task<IReadOnlyList<ChatEntrySlim>> GetEntries(
        long startEntryLid, TimeSpan? maturity, CancellationToken cancellationToken)
    {
        var entries = await ChatsBackend
            .ListNewEntries(ChatId, startEntryLid - 1, MaxEntries, cancellationToken)
            .ConfigureAwait(false);
        var matureBefore = maturity is { } m ? ResumedAt - m : (Moment?)null;
        return entries
            .Where(e => !e.Content.IsNullOrEmpty()
                && (matureBefore is not { } mb || (e.EndsAt ?? e.BeginsAt) <= mb))
            .Select(e => new ChatEntrySlim(e))
            .ToList();
    }

    private static LiveSessionSummary ToLiveSummary(
        ConversationSummary summary, IReadOnlyList<ChatEntrySlim> entries, bool isExpandedByDefault = false)
        => new() {
            Title = summary.Title,
            Description = summary.Description,
            Summary = summary.Summary,
            EndEntryLid = entries[^1].LocalId,
            MessageCount = entries.Count,
            AuthorIds = entries
                .GroupBy(e => e.AuthorId)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .ToArray(),
            IsExpandedByDefault = isExpandedByDefault,
        };

    private static int WordCount(ChatEntrySlim entry)
        => entry.Content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
