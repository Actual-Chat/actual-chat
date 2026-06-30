using ActualChat.Chat.ML;
using ActualChat.Chat.Module;
using ActualChat.Flows;
using ActualChat.Live;
using ActualChat.Streaming;

namespace ActualChat.Chat.Flows;

/// <summary>
/// Throttled resummarization of an in-progress live conversation (at the split-flow cadence). Close-time
/// finalization (materialize into a persisted <see cref="Conversation"/> or vanish) is owned by
/// <c>LiveSessionsBackend</c>; this flow only summarizes while the call is live.
/// </summary>
[Flow(ResumeTimeout = 60)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial class LiveConversationSummaryFlow : Flow<Unit>
{
    private const int MaxEntries = 1000;
    // Resume throttle: kept short so a closing session is materialized and finalized promptly; the actual
    // resummary is additionally gated on Settings.Summarization.ResummarizationDelay.
    private static readonly TimeSpan Throttle = TimeSpan.FromSeconds(20);

    private ChatSettings Settings => field ??= Services.GetRequiredService<ChatSettings>();
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private IConversationSummarizer ConversationSummarizer => field ??= Services.GetRequiredService<IConversationSummarizer>();
    private ILiveSessionsBackend LiveSessionsBackend => field ??= Services.GetRequiredService<ILiveSessionsBackend>();

    private ChatId ChatId => field ??= ChatId.Parse(Id.Arguments);

    [DataMember(Order = 0), MemoryPackOrder(0), Key(0)]
    public long LastSummaryEndLid { get; set; }

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        var live = await LiveSessionsBackend.GetState(ChatId, cancellationToken).ConfigureAwait(false);
        if (live is null || live.IsClosing)
            return; // closed, or closing - the backend owns finalize + materialization now

        // Pre-latch (solo): the normal ConversationSplitFlow owns summarization, so don't double-summarize.
        if (live.SessionStartedAt is null) {
            Runtime.StageResumeIn(Throttle);
            return;
        }

        // Summarize only mature entries (older than ChatEntrySummarizationDelay) so the collapsed block
        // lags behind the newest message — participants keep reading the recent tail uncollapsed.
        var entries = await GetEntries(live.StartEntryLid, matureOnly: true, cancellationToken)
            .ConfigureAwait(false);
        // Like ConversationSplitFlow: wait for enough data before the first LLM call (so the block isn't
        // titled off 2 messages), then throttle re-summaries by ResummarizationDelay.
        var enoughData = entries.Count >= Settings.Summarization.MinConversationEntries
            && entries.Sum(WordCount) >= Settings.Summarization.MinConversationWords;
        var hasNew = entries.Count > 0 && entries[^1].LocalId > LastSummaryEndLid;
        var neverSummarized = LastSummaryEndLid == 0;
        var dueForResummary =
            ResumedAt - live.LastSummaryAt >= Settings.Summarization.ResummarizationDelay;
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

    private async Task<IReadOnlyList<ChatEntrySlim>> GetEntries(
        long startEntryLid, bool matureOnly, CancellationToken cancellationToken)
    {
        var entries = await ChatsBackend
            .ListNewEntries(ChatId, startEntryLid - 1, MaxEntries, cancellationToken)
            .ConfigureAwait(false);
        var matureBefore = ResumedAt - Settings.Summarization.ChatEntrySummarizationDelay;
        return entries
            .Where(e => !e.Content.IsNullOrEmpty()
                && (!matureOnly || (e.EndsAt ?? e.BeginsAt) <= matureBefore))
            .Select(e => new ChatEntrySlim(e))
            .ToList();
    }

    private static LiveSessionSummary ToLiveSummary(ConversationSummary summary, IReadOnlyList<ChatEntrySlim> entries)
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
        };

    private static int WordCount(ChatEntrySlim entry)
        => entry.Content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
