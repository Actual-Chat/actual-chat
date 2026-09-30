using System.Collections.Concurrent;
using ActualChat.Chat.ML;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Chat.Coach;

public partial class CoachAnalysisBackend
{
    private static readonly TimeSpan LiveEntryRetention = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MaxLiveDuration = TimeSpan.FromMinutes(6);

    private readonly ConcurrentDictionary<ChatEntryId, LiveEntry> _live = new();

    private ICoachTranscriptSource TranscriptSource => field ??= Services.GetRequiredService<ICoachTranscriptSource>();
    private IHostApplicationLifetime HostLifetime => field ??= Services.HostLifetime();

    // [ComputeMethod]
    public virtual Task<ApiArray<CoachLiveMark>> ListLiveMarks(
        ChatId chatId, AuthorId authorId, long entryLid, CancellationToken cancellationToken)
        => Task.FromResult(
            _live.TryGetValue(ChatEntryId.New(chatId, entryLid), out var live) && live.AuthorId == authorId
                ? live.Marks
                : ApiArray<CoachLiveMark>.Empty);

    // [EventHandler]
    public virtual async Task OnChatEntryStreamingStartedEvent(
        ChatEntryStreamingStartedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive || !Settings.Coach.IsEnabled)
            return;

        var (entry, author) = eventCommand;
        if (entry is not TextEntry || entry.IsSystemEntry)
            return;
        if (author.UserId.IsGuestOrNull() || author.IsAnonymous == true)
            return;

        await StartLiveTagging(entry, author, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task StartLiveTagging(ChatEntry entry, AuthorFull author, CancellationToken cancellationToken)
    {
        if (!Settings.Coach.IsLiveTaggingEnabled || _live.ContainsKey(entry.Id))
            return;
        var kvas = ServerKvasBackend.ForUser(author.UserId);
        if (!await CoachScopeKvas.IsInScope(kvas, entry.ChatId, cancellationToken).ConfigureAwait(false))
            return;

        var settings = await kvas.UserCoachSettings().Get(cancellationToken).ConfigureAwait(false);
        if (!settings.IsCoachingEnabled)
            return;

        var language = await GetLanguage(entry.Id, author.UserId, cancellationToken).ConfigureAwait(false);
        if (settings.LevelOf(language?.Value) == CoachLanguageLevel.Off)
            return;
        var live = new LiveEntry(entry.AuthorId, author.UserId);
        if (!_live.TryAdd(entry.Id, live))
            return;
        var stopToken = HostLifetime.ApplicationStopping;
        _ = BackgroundTask.Run(() => RunLiveTagging(entry, live, language, stopToken), CancellationToken.None);
    }

    private async Task RunLiveTagging(
        ChatEntry entry, LiveEntry live, Language? language, CancellationToken stopToken)
    {
        LiveTagResult? result = null;
        try {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stopToken);
            cts.CancelAfter(MaxLiveDuration);
            var texts = await TranscriptSource.Open(entry.ContentStreamId, cts.Token)
                .ConfigureAwait(false);
            if (texts is null)
                return;

            var coach = Settings.Coach;
            var isWordSplittable = SpeechTextStats.IsWordSplittable(language);
            var options = new LiveTagOptions(
                coach.TagChunkMinWords,
                coach.TagChunkMaxWords,
                coach.TagChunkContextWords,
                coach.MaxLiveTagChunksPerEntry,
                isWordSplittable);
            result = await SpeechLiveTagger.Run(
                texts,
                async (chunk, context, token) => {
                    if (!TryTakeTaggerCalls(live.UserId, 1))
                        return null;

                    var request = new SpeechTagRequest(chunk, language, context);
                    return (await Tagger.Tag(request, token).ConfigureAwait(false))?.Spans;
                },
                (text, spans) => PublishLiveMarks(entry, live, text, spans, isWordSplittable),
                options,
                cts.Token).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Live tagging of #{EntryId} failed", entry.Id);
        }
        finally {
            live.Done.TrySetResult(result);
            _ = BackgroundTask.Run(() => RemoveLiveEntryLater(entry, live), CancellationToken.None);
        }
    }

    private void PublishLiveMarks(
        ChatEntry entry, LiveEntry live, string text, ApiArray<SpeechSpan> spans, bool isWordSplittable)
    {
        live.Marks = CoachLiveMarks.FromSpans(text, spans, isWordSplittable);
        InvalidateLiveMarks(entry, live);
    }

    private async Task RemoveLiveEntryLater(ChatEntry entry, LiveEntry live)
    {
        await Task.Delay(LiveEntryRetention).ConfigureAwait(false);
        if (_live.TryRemove(new KeyValuePair<ChatEntryId, LiveEntry>(entry.Id, live)))
            InvalidateLiveMarks(entry, live);
    }

    private void InvalidateLiveMarks(ChatEntry entry, LiveEntry live)
    {
        using (Invalidation.Begin())
            _ = ListLiveMarks(entry.ChatId, live.AuthorId, entry.LocalId, default);
    }

    private async Task<SpeechTagResult?> TryGetLiveResult(
        ChatEntryId entryId, string text, CancellationToken cancellationToken)
    {
        // The tags the live tagging produced for exactly this text, or null when there are none to trust: the
        // text settled differently (re-transcription), or some of it could not be tagged
        if (!Settings.Coach.IsLiveTaggingEnabled || !_live.TryGetValue(entryId, out var live))
            return null;

        var whenDone = live.Done.Task;
        if (!whenDone.IsCompleted) {
            var timeout = Task.Delay(Settings.Coach.LiveTaggingResultWait, cancellationToken);
            if (await Task.WhenAny(whenDone, timeout).ConfigureAwait(false) != whenDone)
                return null;
        }
        var result = whenDone.IsCompletedSuccessfully ? whenDone.Result : null;
        return result is { IsComplete: true } && result.Text == text
            ? new SpeechTagResult(result.Spans, Settings.Coach.PromptVersion)
            : null;
    }

    // Nested types

    private sealed class LiveEntry(AuthorId authorId, UserId userId)
    {
        public AuthorId AuthorId { get; } = authorId;
        public UserId UserId { get; } = userId;
        public ApiArray<CoachLiveMark> Marks { get; set; }
        public TaskCompletionSource<LiveTagResult?> Done { get; } = TaskCompletionSourceExt.New<LiveTagResult?>();
    }
}
