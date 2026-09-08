using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Tracks, per armed chat, when voice last started and ended - incoming (from authors other
/// than yourself) and your own, in separate maps. The PTT answer window runs from the end of
/// either; the reply resolver reads the incoming one alone, since it answers "who was talking".
/// </summary>
public class VoiceActivityUI(AppUIHub hub)
    : UIWorkerBase<AppUIHub>(hub), IComputeService, INotifyInitialized
{
    private readonly ConcurrentDictionary<ChatId, Moment> _lastIncomingAt = new();
    private readonly ConcurrentDictionary<ChatId, bool> _liveIncoming = new();
    private readonly ConcurrentDictionary<ChatId, Moment> _lastOwnAt = new();
    private readonly ConcurrentDictionary<ChatId, bool> _liveOwn = new();
    private readonly ConcurrentDictionary<ChatId, bool> _unheardOwn = new();

    private LiveStreamUI LiveStreamUI => Hub.LiveStreamUI;
    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private IAuthors Authors => Hub.Authors;

    public event Action? VoiceStamped;

    void INotifyInitialized.Initialized()
        => this.Start();

    public IReadOnlyDictionary<ChatId, Moment> SnapshotLastIncomingVoiceAt()
        => BuildSnapshot(_lastIncomingAt, [.._liveIncoming.Keys], Clocks.ServerClock.Now);

    public IReadOnlyDictionary<ChatId, Moment> SnapshotLastVoiceAt()
    {
        // Both sides of the conversation arm the reply triggers: a window dated from the other
        // party's utterance alone would be half-consumed by your own reply to it.
        var now = Clocks.ServerClock.Now;
        return MergeSnapshots(
            BuildSnapshot(_lastIncomingAt, [.._liveIncoming.Keys], now),
            BuildSnapshot(_lastOwnAt, [.._liveOwn.Keys], now));
    }

    public bool HasAnyLiveIncoming(IReadOnlyList<ChatId> chatIds)
        => chatIds.Any(_liveIncoming.ContainsKey);

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
            VoiceStamped?.Invoke();
    }

    public void ClearIncomingVoice(ChatId chatId)
    {
        // Stopping a chat's audio must close its answer window too - otherwise the PTT widget
        // recomputes the very same state and the notification the user just dismissed comes back.
        // The live entry goes with it, so neither the snapshot nor the eventual falling edge
        // reopens the window the user just dismissed.
        var wasLive = _liveIncoming.TryRemove(chatId, out _);
        if (_lastIncomingAt.TryRemove(chatId, out _) || wasLive)
            VoiceStamped?.Invoke();
    }

    public void SuppressOwnVoiceWindow(ChatId chatId)
        // Call before closing the recording, not after: the falling edge this suppresses is
        // raised by that very close, so setting the flag afterwards would lose the race.
        => _unheardOwn[chatId] = true;

    public static bool ShouldStamp(bool prevHadOthers, bool nowHasOthers)
        => !prevHadOthers && nowHasOthers;

    public static bool ShouldStampEnd(bool prevHadOthers, bool nowHasOthers)
        => prevHadOthers && !nowHasOthers;

    public static bool ApplyOwnVoiceEnd(
        IDictionary<ChatId, Moment> lastOwnAt,
        ChatId chatId,
        bool isUnheard,
        Moment now)
    {
        // An unheard reply leaves any earlier stamp exactly as it was. It must not extend the
        // window - one false gesture would keep its own window alive for another round - but
        // it must not clear it either: the window a real utterance opened runs to its own end.
        if (isUnheard)
            return false;

        lastOwnAt[chatId] = now;
        return true;
    }

    public static Dictionary<ChatId, Moment> MergeSnapshots(
        Dictionary<ChatId, Moment> incoming,
        IReadOnlyDictionary<ChatId, Moment> own)
    {
        // Latest wins per chat: the window must run from whichever side spoke last, so an old
        // stamp from one side can never shorten a fresh one from the other.
        foreach (var (chatId, at) in own)
            if (!incoming.TryGetValue(chatId, out var incomingAt) || at > incomingAt)
                incoming[chatId] = at;

        return incoming;
    }

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

    // Public so the listening-focus burst manager can share the own-author-filtered signal
    [ComputeMethod]
    public virtual async Task<bool> HasIncomingVoice(ChatId chatId, CancellationToken cancellationToken)
    {
        var authorIds = await LiveStreamUI.GetAudioStreamingAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
        if (authorIds.Count == 0)
            return false;

        var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        var ownAuthorId = ownAuthor?.Id ?? default;
        return authorIds.Any(id => id != ownAuthorId);
    }

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var baseChains = new[] {
            AsyncChain.From(TrackArmedChats),
            AsyncChain.From(TrackOwnVoice),
        };
        var retryDelays = RetryDelaySeq.Exp(0.1, 1);
        return (
            from chain in baseChains
            select chain
                .Log(LogLevel.Debug, Log)
                .RetryForever(retryDelays, Log)
            ).RunIsolated(cancellationToken);
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

    private async Task TrackOwnVoice(CancellationToken cancellationToken)
    {
        // The recording chat id, not the PTT reply: closing by the record button or by the
        // notification action must re-arm the triggers exactly like a gesture does, and every
        // one of those paths ends here.
        var cRecordingChatId = await Computed
            .Capture(ChatAudioUI.GetRecordingChatId, cancellationToken)
            .ConfigureAwait(false);
        // Seeded from the live set so a chain restarted mid-recording (retry after a transient
        // fault) still sees the falling edge; a leaked live entry would report a forever-fresh
        // stamp and hold the answer window open for the rest of the scope's life.
        var prevChatId = _liveOwn.Keys.FirstOrDefault();
        await foreach (var change in cRecordingChatId.Changes(cancellationToken).ConfigureAwait(false)) {
            var chatId = change.Value;
            if (prevChatId == chatId)
                continue;

            if (prevChatId is { } prevValue)
                EndOwnVoice(prevValue);
            if (chatId is { } value) {
                _liveOwn[value] = true;
                _unheardOwn.TryRemove(value, out _);
            }
            prevChatId = chatId;
        }
    }

    private void EndOwnVoice(ChatId chatId)
    {
        var wasLive = _liveOwn.TryRemove(chatId, out _);
        var isUnheard = _unheardOwn.TryRemove(chatId, out _);
        var hasStamped = ApplyOwnVoiceEnd(_lastOwnAt, chatId, isUnheard, Clocks.ServerClock.Now);
        if (hasStamped || wasLive)
            VoiceStamped?.Invoke();
    }
}
