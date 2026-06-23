using ActualChat.Chat.ML;
using ActualChat.Chat.Module;
using ActualChat.Flows;
using ActualChat.Live;
using ActualChat.Notifications;
using ActualChat.Queues;
using ActualChat.Streaming;

namespace ActualChat.Chat.Flows;

/// <summary>
/// Throttled resummarization of an in-progress live conversation, and its finalization on close:
/// materialize into a persisted <see cref="Conversation"/> when it meets the split threshold, else vanish.
/// </summary>
[Flow(ResumeTimeout = 60)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial class LiveConversationSummaryFlow : Flow<Unit>
{
    private const int MaxEntries = 1000;
    private static readonly TimeSpan Throttle = TimeSpan.FromSeconds(20);

    private ChatSettings Settings => field ??= Services.GetRequiredService<ChatSettings>();
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private IConversationSummarizer ConversationSummarizer => field ??= Services.GetRequiredService<IConversationSummarizer>();
    private ILiveSessionsBackend LiveSessionsBackend => field ??= Services.GetRequiredService<ILiveSessionsBackend>();

    private ChatId ChatId => field ??= ChatId.Parse(Id.Arguments);

    [DataMember(Order = 0), MemoryPackOrder(0), Key(0)]
    public long LastSummaryEndLid { get; set; }
    [DataMember(Order = 1), MemoryPackOrder(1), Key(1)]
    public bool TitledNotificationSent { get; set; }

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        var live = await LiveSessionsBackend.Get(ChatId, cancellationToken).ConfigureAwait(false);
        if (live is null)
            return; // Already closed elsewhere

        if (live.IsClosing) {
            await Finalize(live, cancellationToken).ConfigureAwait(false);
            return;
        }

        var entries = await GetEntries(live.StartEntryLid, cancellationToken).ConfigureAwait(false);
        var hasEnoughNew = entries.Count > 0 && entries[^1].LocalId > LastSummaryEndLid;
        if (hasEnoughNew) {
            var result = await ConversationSummarizer.Summarize(entries, cancellationToken).ConfigureAwait(false);
            if (result.Summary is { } summary) {
                await LiveSessionsBackend
                    .UpdateSummary(ChatId, ToLiveSummary(summary, entries), cancellationToken)
                    .ConfigureAwait(false);
                LastSummaryEndLid = entries[^1].LocalId;
                if (!TitledNotificationSent && !summary.Title.IsNullOrEmpty()) {
                    await Notify(live, ConversationNotificationPhase.Titled, $"Voice chat: {summary.Title}", cancellationToken).ConfigureAwait(false);
                    TitledNotificationSent = true;
                }
            }
        }

        Runtime.StageResumeIn(Throttle);
    }

    // Private methods

    private async Task Finalize(LiveConversation live, CancellationToken cancellationToken)
    {
        var entries = await GetEntries(live.StartEntryLid, cancellationToken).ConfigureAwait(false);
        var wordCount = entries.Sum(WordCount);
        var meetsThreshold = entries.Count >= Settings.Summarization.MinConversationEntries
            && wordCount >= Settings.Summarization.MinConversationWords;
        if (meetsThreshold) {
            var range = new Range<long>(live.StartEntryLid, entries[^1].LocalId + 1);
            await Services.Queues()
                .Enqueue(new ConversationBackend_Summarize(ChatId, [range]) { IsLiveMaterialization = true }, cancellationToken)
                .ConfigureAwait(false);
        }
        var finalContent = live.Title.IsNullOrEmpty() ? "Voice chat ended" : $"Voice chat ended: {live.Title}";
        await Notify(live, ConversationNotificationPhase.Final, finalContent, cancellationToken).ConfigureAwait(false);
        await LiveSessionsBackend.Close(ChatId, cancellationToken).ConfigureAwait(false);
    }

    private Task Notify(LiveConversation live, ConversationNotificationPhase phase, string text, CancellationToken cancellationToken)
        => Services.Queues()
            .Enqueue(
                new NotificationsBackend_NotifyConversation(live.ConversationId, phase, text, live.EndEntryLid, live.AuthorIds),
                cancellationToken);

    private async Task<IReadOnlyList<ChatEntrySlim>> GetEntries(long startEntryLid, CancellationToken cancellationToken)
    {
        var entries = await ChatsBackend
            .ListNewEntries(ChatId, startEntryLid - 1, MaxEntries, cancellationToken)
            .ConfigureAwait(false);
        return entries
            .Where(e => !e.Content.IsNullOrEmpty())
            .Select(e => new ChatEntrySlim(e))
            .ToList();
    }

    private static LiveConversationSummary ToLiveSummary(ConversationSummary summary, IReadOnlyList<ChatEntrySlim> entries)
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
