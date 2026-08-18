using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Tracks, per armed chat, the last time INCOMING voice (from authors other than yourself)
/// started arriving, so the PTT reply resolver can pick the chat that most recently spoke.
/// </summary>
public class IncomingVoiceActivityUI(AppUIHub hub)
    : UIWorkerBase<AppUIHub>(hub), IComputeService, INotifyInitialized
{
    private readonly ConcurrentDictionary<ChatId, Moment> _lastIncomingAt = new();

    private LiveStreamUI LiveStreamUI => Hub.LiveStreamUI;
    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private IAuthors Authors => Hub.Authors;

    public event Action? IncomingVoiceStamped;

    public static bool ShouldStamp(bool prevHadOthers, bool nowHasOthers)
        => !prevHadOthers && nowHasOthers;

    void INotifyInitialized.Initialized()
        => this.Start();

    public IReadOnlyDictionary<ChatId, Moment> SnapshotLastIncomingVoiceAt()
        => new Dictionary<ChatId, Moment>(_lastIncomingAt);

    public void NoteIncomingVoice(ChatId chatId, Moment at)
    {
        // The wake path replays an utterance that may be over already, so HasIncomingVoice never
        // sees an edge for it - without this stamp such a wake opens no answer window at all.
        // The stamp is a plain dictionary write; the event is the only thing that lets GestureUI
        // arm sooner than its next poll.
        var now = Clocks.ServerClock.Now;
        var stampedAt = at > now ? now : at;
        var hasAdvanced = false;
        _lastIncomingAt.AddOrUpdate(
            chatId,
            _ => {
                hasAdvanced = true;
                return stampedAt;
            },
            (_, oldAt) => {
                if (oldAt >= stampedAt)
                    return oldAt;

                hasAdvanced = true;
                return stampedAt;
            });
        if (hasAdvanced)
            IncomingVoiceStamped?.Invoke();
    }

    public void ClearIncomingVoice(ChatId chatId)
    {
        // Stopping a chat's audio must close its answer window too - otherwise the PTT widget
        // recomputes the very same state and the notification the user just dismissed comes back.
        if (_lastIncomingAt.TryRemove(chatId, out _))
            IncomingVoiceStamped?.Invoke();
    }

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var retryDelays = RetryDelaySeq.Exp(0.1, 1);
        return AsyncChain.From(TrackArmedChats)
            .Log(LogLevel.Debug, Log)
            .RetryForever(retryDelays, Log)
            .RunIsolated(cancellationToken);
    }

    // Protected/internal methods

    [ComputeMethod]
    protected virtual async Task<bool> HasIncomingVoice(ChatId chatId, CancellationToken cancellationToken)
    {
        var authorIds = await LiveStreamUI.GetAudioStreamingAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
        if (authorIds.Count == 0)
            return false;

        var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        var ownAuthorId = ownAuthor?.Id ?? default;
        return authorIds.Any(id => id != ownAuthorId);
    }

    // Private methods

    private async Task TrackArmedChats(CancellationToken cancellationToken)
    {
        var cArmedChats = await Computed
            .Capture(() => ChatAudioUI.GetPttChatIds(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var watchers = new Dictionary<ChatId, FuncWorker>();
        await foreach (var change in cArmedChats.Changes(cancellationToken).ConfigureAwait(false)) {
            var armedChats = change.Value.ToHashSet();
            var toStop = watchers.Keys.Except(armedChats).ToList();
            var toStart = armedChats.Except(watchers.Keys).ToList();

            foreach (var chatId in toStop)
                if (watchers.Remove(chatId, out var watcher))
                    await watcher.Stop().ConfigureAwait(false);
            foreach (var chatId in toStart)
                watchers[chatId] = FuncWorker.Start(ct => WatchChat(chatId, ct), cancellationToken);
        }
    }

    private Task WatchChat(ChatId chatId, CancellationToken cancellationToken)
    {
        var retryDelays = RetryDelaySeq.Exp(0.1, 1);
        return AsyncChain.From(ct => WatchChatOnce(chatId, ct))
            .Log(LogLevel.Debug, Log)
            .RetryForever(retryDelays, Log)
            .Run(cancellationToken);
    }

    private async Task WatchChatOnce(ChatId chatId, CancellationToken cancellationToken)
    {
        var cHasOthers = await Computed
            .Capture(() => HasIncomingVoice(chatId, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var prevHadOthers = false;
        await foreach (var change in cHasOthers.Changes(cancellationToken).ConfigureAwait(false)) {
            var nowHasOthers = change.Value;
            if (ShouldStamp(prevHadOthers, nowHasOthers))
                NoteIncomingVoice(chatId, Clocks.ServerClock.Now);
            prevHadOthers = nowHasOthers;
        }
    }
}
