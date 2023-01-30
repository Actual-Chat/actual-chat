using ActualChat.Audio;
using ActualChat.Audio.UI.Blazor.Components;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public class AudioUI : WorkerBase
{
    private Language? _lastRecordingLanguage;
    private ChatId _lastRecordingChatId;
    private ChatId _lastRecorderChatId;
    private readonly IMutableState<Moment?> _stopRecordingAt;

    private Session Session { get; }
    private AudioSettings AudioSettings { get; }
    private AudioRecorder AudioRecorder { get; }
    private ChatPlayers ChatPlayers { get; }
    private IdleAudioMonitor IdleRecordingMonitor { get; }
    private IdleAudioMonitor IdleListeningMonitor { get; }
    private IChats Chats { get; }
    private LanguageUI LanguageUI { get; }
    private TuneUI TuneUI { get; }
    private ActiveChatsUI ActiveChatsUI { get; }
    private InteractiveUI InteractiveUI { get; }
    private UICommander UICommander { get; }
    private MomentClockSet Clocks { get; }
    private ILogger Log { get; }

    private Moment Now => Clocks.SystemClock.Now;
    public IState<Moment?> StopRecordingAt => _stopRecordingAt;

    public AudioUI(IServiceProvider services)
    {
        Session = services.GetRequiredService<Session>();
        AudioSettings = services.GetRequiredService<AudioSettings>();
        AudioRecorder = services.GetRequiredService<AudioRecorder>();
        ChatPlayers = services.GetRequiredService<ChatPlayers>();
        IdleRecordingMonitor = services.GetRequiredService<IdleAudioMonitor>();
        IdleListeningMonitor = services.GetRequiredService<IdleAudioMonitor>();
        Clocks = services.Clocks();
        Chats = services.GetRequiredService<IChats>();
        LanguageUI = services.GetRequiredService<LanguageUI>();
        InteractiveUI = services.GetRequiredService<InteractiveUI>();
        TuneUI = services.GetRequiredService<TuneUI>();
        ActiveChatsUI = services.GetRequiredService<ActiveChatsUI>();
        UICommander = services.UICommander();
        Log = services.LogFor(GetType());

        // Read entry states from other windows / devices are delayed by 1s
        _stopRecordingAt = services.StateFactory().NewMutable<Moment?>();
        Start();
    }

    [ComputeMethod] // Synced
    public virtual Task<ChatAudioState> GetState(ChatId chatId)
    {
        if (chatId.IsNone)
            return Task.FromResult(ChatAudioState.None);

        var activeChats = ActiveChatsUI.ActiveChats.Value;
        activeChats.TryGetValue(chatId, out var activeChat);
        var isListening = activeChat.IsListening;
        var isRecording = activeChat.IsRecording;
        var isPlayingHistorical = ChatPlayers.PlaybackState.Value is HistoricalPlaybackState hps && hps.ChatId == chatId;
        var result = new ChatAudioState(chatId, isListening, isPlayingHistorical, isRecording);
        return Task.FromResult(result);
    }

    [ComputeMethod] // Synced
    public virtual Task<ImmutableHashSet<ChatId>> GetListeningChatIds()
        => Task.FromResult(ActiveChatsUI.ActiveChats.Value.Where(c => c.IsListening).Select(c => c.ChatId).ToImmutableHashSet());

    public ValueTask SetListeningState(ChatId chatId, bool mustListen)
    {
        if (chatId.IsNone)
            return ValueTask.CompletedTask;

        return ActiveChatsUI.UpdateActiveChats(activeChats => {
            var oldActiveChats = activeChats;
            if (activeChats.TryGetValue(chatId, out var chat) && chat.IsListening != mustListen) {
                activeChats = activeChats.Remove(chat);
                chat = chat with { IsListening = mustListen };
                activeChats = activeChats.Add(chat);
            }
            else if (mustListen)
                activeChats = activeChats.Add(new ActiveChat(chatId, true, false, Now));
            if (oldActiveChats != activeChats)
                UICommander.RunNothing();

            return activeChats;
        });
    }

    public ValueTask ClearListeningState()
        => ActiveChatsUI.UpdateActiveChats(activeChats => {
            var oldActiveChats = activeChats;
            foreach (var chat in oldActiveChats) {
                if (chat.IsListening) {
                    activeChats = activeChats.Remove(chat);
                    var updatedChat = chat with { IsListening = false };
                    activeChats = activeChats.Add(updatedChat);
                }
            }
            if (oldActiveChats != activeChats)
                UICommander.RunNothing();

            return activeChats;
        });

    [ComputeMethod] // Synced
    public virtual Task<ChatId> GetRecordingChatId()
        => Task.FromResult(ActiveChatsUI.ActiveChats.Value.FirstOrDefault(c => c.IsRecording).ChatId);

    public ValueTask SetRecordingChatId(ChatId chatId)
        => ActiveChatsUI.UpdateActiveChats(activeChats => {
            var oldChat = activeChats.FirstOrDefault(c => c.IsRecording);
            if (oldChat.ChatId == chatId)
                return activeChats;

            if (!oldChat.ChatId.IsNone)
                activeChats = activeChats.AddOrUpdate(oldChat with {
                    IsRecording = false,
                    Recency = Now,
                });
            if (!chatId.IsNone) {
                var newChat = new ActiveChat(chatId, true, true, Now);
                activeChats = activeChats.AddOrUpdate(newChat);
                TuneUI.Play("begin-recording");
            }
            else
                TuneUI.Play("end-recording");

            UICommander.RunNothing();
            return activeChats;
        });

    [ComputeMethod]
    public virtual async Task<RealtimePlaybackState?> GetExpectedRealtimePlaybackState()
    {
        var listeningChatIds = await GetListeningChatIds().ConfigureAwait(false);
        return listeningChatIds.Count == 0 ? null : new RealtimePlaybackState(listeningChatIds);
    }

    protected override Task RunInternal(CancellationToken cancellationToken)
    {
        var baseChains = new AsyncChain[] {
            new(nameof(InvalidateActiveChatDependencies), InvalidateActiveChatDependencies),
            new(nameof(InvalidateHistoricalPlaybackDependencies), InvalidateHistoricalPlaybackDependencies),
            new(nameof(PushRealtimePlaybackState), PushRealtimePlaybackState),
            new(nameof(SyncRecordingState), SyncRecordingState),
            new(nameof(StopRecordingWhenIdle), StopRecordingWhenIdle),
            new(nameof(StopListeningWhenIdle), StopListeningWhenIdle),
        };
        var retryDelays = new RetryDelaySeq(100, 1000);
        return (
            from chain in baseChains
            select chain
                .RetryForever(retryDelays, Log)
            // .LogBoundary(LogLevel.Debug, Log)
            ).RunIsolated(cancellationToken);
    }

    // Private methods

    private async Task InvalidateActiveChatDependencies(CancellationToken cancellationToken)
    {
        var oldRecordingChat = default(ActiveChat);
        var oldListeningChats = new HashSet<ActiveChat>();
        var changes = ActiveChatsUI.ActiveChats.Changes(cancellationToken);
        await foreach (var cActiveContacts in changes.ConfigureAwait(false)) {
            var activeChats = cActiveContacts.Value;
            var newRecordingChat = activeChats.FirstOrDefault(c => c.IsRecording);
            var newListeningChats = activeChats.Where(c => c.IsListening).ToHashSet();

            Log.LogDebug("InvalidateActiveChatDependencies: *");
            var added = newListeningChats.Except(oldListeningChats);
            var removed = oldListeningChats.Except(newListeningChats);
            var changed = added.Concat(removed).ToList();
            using (Computed.Invalidate()) {
                if (newRecordingChat != oldRecordingChat) {
                    _ = GetRecordingChatId();
                    _ = GetState(oldRecordingChat.ChatId);
                    _ = GetState(newRecordingChat.ChatId);
                }
                if (changed.Count > 0) {
                    _ = GetListeningChatIds();
                    foreach (var c in changed)
                        _ = GetState(c.ChatId);
                }
            }

            oldRecordingChat = newRecordingChat;
            oldListeningChats = newListeningChats;
        }
    }

    private async Task InvalidateHistoricalPlaybackDependencies(CancellationToken cancellationToken)
    {
        var oldChatId = ChatId.None;
        var changes = ChatPlayers.PlaybackState.Changes(cancellationToken);
        await foreach (var cPlaybackState in changes.ConfigureAwait(false)) {
            var newChatId = (cPlaybackState.Value as HistoricalPlaybackState)?.ChatId ?? default;
            if (newChatId == oldChatId)
                continue;

            Log.LogDebug("InvalidateHistoricalPlaybackDependencies: *");
            using (Computed.Invalidate()) {
                _ = GetState(oldChatId);
                _ = GetState(newChatId);
            }

            oldChatId = newChatId;
        }
    }

    private async Task StopRecordingWhenIdle(CancellationToken cancellationToken)
    {
        var options = new IdleAudioMonitor.Options(AudioSettings.IdleRecordingTimeout,
            AudioSettings.IdleRecordingTimeoutBeforeCountdown,
            AudioSettings.IdleRecordingCheckInterval);

        var cChatId = await Computed.Capture(GetRecordingChatId).ConfigureAwait(false);
        var prevChatId = cChatId.Value;
        await foreach (var change in cChatId.Changes(cancellationToken).ConfigureAwait(false)) {
            var chatId = change.Value;
            if (!prevChatId.IsNone)
                await IdleRecordingMonitor.StopMonitoring(prevChatId).ConfigureAwait(false);
            if (!chatId.IsNone)
                IdleRecordingMonitor.StartMonitoring(chatId, OnIdleChanged, options, cancellationToken);
            prevChatId = chatId;
        }

        Task OnIdleChanged(ChatId _, IdleAudioMonitor.State state)
        {
            _stopRecordingAt.Value = state.WillBeIdleAt;
            return state.IsIdle ? UpdateRecorderState(true, default, cancellationToken) : Task.CompletedTask;
        }
    }

    private async Task StopListeningWhenIdle(CancellationToken cancellationToken)
    {
        var options = new IdleAudioMonitor.Options(AudioSettings.IdleListeningTimeout,
            AudioSettings.IdleListeningTimeout - AudioSettings.IdleListeningCheckInterval + TimeSpan.FromSeconds(1),
            AudioSettings.IdleListeningCheckInterval);
        var cListeningChatIds = await Computed.Capture(GetListeningChatIds).ConfigureAwait(false);
        var prevListeningChatIds = cListeningChatIds.Value;
        await foreach (var change in cListeningChatIds.Changes(cancellationToken)) {
            var listeningChatIds = change.Value;
            var toStop = prevListeningChatIds.Except(listeningChatIds).ToList();
            var toStart = listeningChatIds.Except(prevListeningChatIds).ToList();
            await IdleListeningMonitor.StopMonitoring(toStop).ConfigureAwait(false);
            IdleListeningMonitor.StartMonitoring(toStart, OnIdleChanged, options, cancellationToken);
            prevListeningChatIds = listeningChatIds;
        }

        Task OnIdleChanged(ChatId chatId, IdleAudioMonitor.State state)
            => state.IsIdle ? SetListeningState(chatId, false).AsTask() : Task.CompletedTask;
    }

    private async Task SyncRecordingState(CancellationToken cancellationToken)
    {
        var cRecordingChatId = await Computed
            .Capture(() => SyncRecordingStateImpl(cancellationToken))
            .ConfigureAwait(false);
        // Let's update it continuously -- solely for the side effects of GetRecordingChatId runs
        await cRecordingChatId.When(_ => false, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    protected virtual async Task<Symbol> SyncRecordingStateImpl(CancellationToken cancellationToken)
    {
        // This compute method creates dependencies & gets recomputed on changes by SyncRecordingState.
        // The result it returns doesn't have any value - it runs solely for its own side effects.

        var recordingChatId = await GetRecordingChatId().ConfigureAwait(false);
        var recordingChatIdChanged = recordingChatId != _lastRecordingChatId;
        _lastRecordingChatId = recordingChatId;

        var recorderState = await AudioRecorder.State.Use(cancellationToken).ConfigureAwait(false);
        var recorderChatId = recorderState?.ChatId ?? default;
        var recorderChatIdChanged = recorderChatId != _lastRecorderChatId;
        _lastRecorderChatId = recorderChatId;

        if (recordingChatId == recorderChatId) {
            // The state is in sync
            if (recordingChatId.IsNone)
                return default;

            if (await IsRecordingLanguageChanged().ConfigureAwait(false))
                SyncRecorderState(); // We need to toggle the recording in this case
        } else if (recordingChatIdChanged) {
            // The recording was activated or deactivated
            SyncRecorderState();
            if (recordingChatId.IsNone)
                return default;

            // Update _lastRecordingLanguage
            await IsRecordingLanguageChanged().ConfigureAwait(false);
            // Start recording = start realtime playback
            await SetListeningState(recordingChatId, true).ConfigureAwait(false);
        } else if (recorderChatIdChanged) {
            // Something stopped (or started?) the recorder
            await SetRecordingChatId(recorderChatId).ConfigureAwait(false);
        }
        return default;

        async ValueTask<bool> IsRecordingLanguageChanged()
        {
            if (recorderChatId.IsNone)
                return false;

            var language = await LanguageUI.GetChatLanguage(recorderChatId, cancellationToken).ConfigureAwait(false);
            var isLanguageChanged = _lastRecordingLanguage.HasValue && language != _lastRecordingLanguage;
            _lastRecordingLanguage = language;
            return isLanguageChanged;
        }

        void SyncRecorderState()
            => UpdateRecorderState(recorderState != null && recorderChatId != recordingChatId, recordingChatId, cancellationToken);
    }

    private Task UpdateRecorderState(
        bool mustStop,
        ChatId recordingChatId,
        CancellationToken cancellationToken)
        => BackgroundTask.Run(async () => {
            if (mustStop) {
                // Recording is running - let's top it first;
                await AudioRecorder.StopRecording(cancellationToken).ConfigureAwait(false);
            }
            if (!recordingChatId.IsNone) {
                // And start the recording if we must
                if (!InteractiveUI.IsInteractive.Value) {
                    var isConfirmed = false;
                    var chat = await Chats.Get(Session, recordingChatId, cancellationToken).ConfigureAwait(false);
                    if (chat != null) {
                        var operation = $"recording in \"{chat.Title}\"";
                        isConfirmed = await InteractiveUI.Demand(operation, cancellationToken).ConfigureAwait(false);
                    }
                    if (!isConfirmed) {
                        await SetRecordingChatId(ChatId.None).ConfigureAwait(false);
                        // An extra pause to make sure we don't apply changes too frequently
                        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                        return;
                    }
                }
                await AudioRecorder.StartRecording(recordingChatId, cancellationToken).ConfigureAwait(false);
            }
        }, Log, "Failed to apply new recording state.", CancellationToken.None);

    private async Task PushRealtimePlaybackState(CancellationToken cancellationToken)
    {
        using var dCancellationTask = cancellationToken.ToTask();
        var cancellationTask = dCancellationTask.Resource;

        var playbackState = ChatPlayers.PlaybackState;
        var cExpectedPlaybackState = await Computed
            .Capture(GetExpectedRealtimePlaybackState)
            .ConfigureAwait(false);
        var cActualPlaybackState = playbackState.Computed;

        while (!cancellationToken.IsCancellationRequested) {
            var expectedPlaybackState = cExpectedPlaybackState.Value;
            var actualPlaybackState = cActualPlaybackState.Value;
            if (actualPlaybackState is null or RealtimePlaybackState) {
                if (!ReferenceEquals(actualPlaybackState, expectedPlaybackState)) {
                    if (expectedPlaybackState == null) {
                        Log.LogDebug("PushRealtimePlaybackState: stopping playback");
                        ChatPlayers.StopPlayback();
                    }
                    else {
                        if (actualPlaybackState == null && !InteractiveUI.IsInteractive.Value) {
                            var isConfirmed = false;
                            var chats = await expectedPlaybackState.ChatIds
                                .Select(id => Chats.Get(Session, id, cancellationToken))
                                .Collect()
                                .ConfigureAwait(false);
                            var chatTitles = chats
                                .SkipNullItems()
                                .Select(c => $"\"{c.Title}\"")
                                .ToCommaPhrase();
                            if (!chatTitles.IsNullOrEmpty()) { // It's empty if there are no chats
                                var operation = "listening in " + chatTitles;
                                isConfirmed = await InteractiveUI.Demand(operation, cancellationToken).ConfigureAwait(false);
                            }
                            if (!isConfirmed) {
                                await ClearListeningState().ConfigureAwait(false);
                                // An extra pause to make sure we don't apply changes too frequently
                                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                                continue;
                            }
                        }
                        Log.LogDebug("PushRealtimePlaybackState: starting playback");
                        ChatPlayers.StartRealtimePlayback(expectedPlaybackState);
                    }

                    // An extra pause to make sure we don't apply changes too frequently
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }
            }

            Log.LogDebug("PushRealtimePlaybackState: waiting for changes");
            await Task.WhenAny(
                cActualPlaybackState.WhenInvalidated(cancellationToken),
                cExpectedPlaybackState.WhenInvalidated(cancellationToken),
                cancellationTask
            ).ConfigureAwait(false);
            cExpectedPlaybackState = await cExpectedPlaybackState.Update(cancellationToken).ConfigureAwait(false);
            cActualPlaybackState = playbackState.Computed;
        }
        // ReSharper disable once FunctionNeverReturns
    }
}
