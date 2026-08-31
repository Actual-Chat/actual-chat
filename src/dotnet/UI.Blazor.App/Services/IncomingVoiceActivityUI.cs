using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Tracks, per armed chat, when INCOMING voice (from authors other than yourself) last started
/// and ended, so the PTT answer window runs from the end of the utterance and the reply
/// resolver can pick the chat that most recently spoke.
/// </summary>
public class IncomingVoiceActivityUI(AppUIHub hub)
    : UIWorkerBase<AppUIHub>(hub), IComputeService, INotifyInitialized
{
    private readonly ConcurrentDictionary<ChatId, Moment> _lastIncomingAt = new();
    private readonly ConcurrentDictionary<ChatId, bool> _liveIncoming = new();

    private LiveStreamUI LiveStreamUI => Hub.LiveStreamUI;
    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private IAuthors Authors => Hub.Authors;

    public event Action? IncomingVoiceStamped;

    void INotifyInitialized.Initialized()
        => this.Start();

    public IReadOnlyDictionary<ChatId, Moment> SnapshotLastIncomingVoiceAt()
        => BuildSnapshot(_lastIncomingAt, [.._liveIncoming.Keys], Clocks.ServerClock.Now);

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
        // The live entry goes with it, so neither the snapshot nor the eventual falling edge
        // reopens the window the user just dismissed.
        var wasLive = _liveIncoming.TryRemove(chatId, out _);
        if (_lastIncomingAt.TryRemove(chatId, out _) || wasLive)
            IncomingVoiceStamped?.Invoke();
    }

    public static bool ShouldStamp(bool prevHadOthers, bool nowHasOthers)
        => !prevHadOthers && nowHasOthers;

    public static bool ShouldStampEnd(bool prevHadOthers, bool nowHasOthers)
        => prevHadOthers && !nowHasOthers;

    public static Dictionary<ChatId, Moment> BuildSnapshot(
        IReadOnlyDictionary<ChatId, Moment> lastIncomingAt,
        IReadOnlyCollection<ChatId> liveChatIds,
        Moment now)
    {
        // A chat still streaming reports a fresh stamp, so the answer window can't lapse
        // mid-utterance however short it is; the real end stamp lands on the falling edge.
        var snapshot = new Dictionary<ChatId, Moment>(lastIncomingAt);
        foreach (var chatId in liveChatIds)
            snapshot[chatId] = now;
        return snapshot;
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
                if (watchers.Remove(chatId, out var watcher)) {
                    await watcher.Stop().ConfigureAwait(false);
                    // Nothing watches a disarmed chat, so a leftover live entry would report a
                    // forever-fresh stamp if the chat is ever re-armed.
                    _liveIncoming.TryRemove(chatId, out _);
                }
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
        // Seeded from the live set so a watcher restarted mid-utterance (retry after a transient
        // fault) still sees the falling edge and doesn't leak a forever-live entry.
        var prevHadOthers = _liveIncoming.ContainsKey(chatId);
        await foreach (var change in cHasOthers.Changes(cancellationToken).ConfigureAwait(false)) {
            var nowHasOthers = change.Value;
            if (ShouldStamp(prevHadOthers, nowHasOthers)) {
                _liveIncoming[chatId] = true;
                NoteIncomingVoice(chatId, Clocks.ServerClock.Now);
            }
            else if (ShouldStampEnd(prevHadOthers, nowHasOthers)) {
                // TryRemove failing means ClearIncomingVoice closed this window on purpose;
                // stamping the end anyway would reopen it.
                if (_liveIncoming.TryRemove(chatId, out _))
                    NoteIncomingVoice(chatId, Clocks.ServerClock.Now);
            }
            prevHadOthers = nowHasOthers;
        }
    }
}
