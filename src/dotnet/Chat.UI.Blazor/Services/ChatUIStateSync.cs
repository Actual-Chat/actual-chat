using ActualChat.Audio;
using ActualChat.Audio.UI.Blazor.Components;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatUIStateSync : WorkerBase
{
    // All properties are resolved in lazy fashion because otherwise we'll get a dependency cycle
    private IChats? _chats;
    private ChatPlayers? _chatPlayers;
    private AudioRecorder? _audioRecorder;
    private AudioSettings? _audioSettings;
    private AccountSettings? _accountSettings;
    private KeepAwakeUI? _keepAwakeUI;
    private ChatUI? _chatUI;

    private LanguageId? _lastRecordingLanguage;
    private Symbol _lastRecordingChatId;
    private Symbol _lastRecorderChatId;

    private Session Session { get; }
    private IServiceProvider Services { get; }
    private MomentClockSet Clocks { get; }
    private ILogger Log { get; }
    private LanguageUI LanguageUI { get; }

    private IChats Chats => _chats ??= Services.GetRequiredService<IChats>();
    private ChatPlayers ChatPlayers => _chatPlayers ??= Services.GetRequiredService<ChatPlayers>();
    private AudioRecorder AudioRecorder => _audioRecorder ??= Services.GetRequiredService<AudioRecorder>();
    private AudioSettings AudioSettings => _audioSettings ??= Services.GetRequiredService<AudioSettings>();
    private AccountSettings AccountSettings => _accountSettings ??= Services.GetRequiredService<AccountSettings>();
    private KeepAwakeUI KeepAwakeUI => _keepAwakeUI ??= Services.GetRequiredService<KeepAwakeUI>();
    private ChatUI ChatUI => _chatUI ??= Services.GetRequiredService<ChatUI>();
    private InteractiveUI InteractiveUI { get; }

    public ChatUIStateSync(Session session, IServiceProvider services)
    {
        Session = session;
        Services = services;
        Log = services.LogFor(GetType());
        Clocks = services.Clocks();
        LanguageUI = services.GetRequiredService<LanguageUI>();
        InteractiveUI = services.GetRequiredService<InteractiveUI>();
    }

    // Protected methods

    protected override Task RunInternal(CancellationToken cancellationToken)
        => Task.WhenAll(
            InvalidateSelectedChatDependencies(cancellationToken),
            InvalidatePinnedChatsDependencies(cancellationToken),
            InvalidateActiveChatsDependencies(cancellationToken),
            SyncPlaybackState(cancellationToken),
            SyncRecordingState(cancellationToken),
            SyncKeepAwakeState(cancellationToken),
            StopRecordingWhenInactive(cancellationToken),
            ResetHighlightedChatEntry(cancellationToken),
            Task.CompletedTask); // Just to add more items w/o need to worry about comma :)

    private async Task InvalidateSelectedChatDependencies(CancellationToken cancellationToken)
    {
        var oldSelectedChatId = ChatUI.SelectedChatId.Value;
        var changes = ChatUI.SelectedChatId.Changes(FixedDelayer.ZeroUnsafe, cancellationToken);
        await foreach (var cSelectedChatId in changes.ConfigureAwait(false)) {
            var newSelectedChatId = cSelectedChatId.Value;

            using (Computed.Invalidate()) {
                _ = ChatUI.IsSelected(oldSelectedChatId);
                _ = ChatUI.IsSelected(newSelectedChatId);
            }

            oldSelectedChatId = newSelectedChatId;
        }
    }

    private async Task InvalidatePinnedChatsDependencies(CancellationToken cancellationToken)
    {
        var oldPinnedChatIds = new HashSet<Symbol>();
        var changes = ChatUI.PinnedChats.Changes(FixedDelayer.ZeroUnsafe, cancellationToken);
        await foreach (var cPinnedChats in changes.ConfigureAwait(false)) {
            var newPinnedChatIds = cPinnedChats.Value.Select(c => c.ChatId).ToHashSet();

            var addedChatIds = newPinnedChatIds.Except(oldPinnedChatIds);
            var removedChatIds = oldPinnedChatIds.Except(newPinnedChatIds);
            var changedChatIds = addedChatIds.Concat(removedChatIds);
            using (Computed.Invalidate()) {
                foreach (var chatId in changedChatIds)
                    _ = ChatUI.IsPinned(chatId);
            }

            oldPinnedChatIds = newPinnedChatIds;
        }
    }

    private async Task InvalidateActiveChatsDependencies(CancellationToken cancellationToken)
    {
        var oldRecordingChatId = Symbol.Empty;
        var oldListeningChatIds = new HashSet<Symbol>();
        var changes = ChatUI.ActiveChats.Changes(FixedDelayer.ZeroUnsafe, cancellationToken);
        await foreach (var cActiveChats in changes.ConfigureAwait(false)) {
            var activeChats = cActiveChats.Value;
            var newRecordingChatId = activeChats.FirstOrDefault(c => c.IsRecording).ChatId;
            var newListeningChatIds = activeChats.Where(c => c.IsListening).Select(c => c.ChatId).ToHashSet();

            var addedListeningChatIds = newListeningChatIds.Except(oldListeningChatIds);
            var removedListeningChatIds = oldListeningChatIds.Except(newListeningChatIds);
            var changedListeningChatIds = addedListeningChatIds.Concat(removedListeningChatIds).ToList();
            using (Computed.Invalidate()) {
                if (newRecordingChatId != oldRecordingChatId) {
                    _ = ChatUI.RecordingChatId();
                    _ = ChatUI.IsRecording(oldRecordingChatId);
                    _ = ChatUI.IsRecording(newRecordingChatId);
                }
                if (changedListeningChatIds.Count > 0) {
                    _ = ChatUI.ListeningChatIds();
                    foreach (var id in changedListeningChatIds)
                        _ = ChatUI.IsListening(id);
                }
            }

            oldRecordingChatId = newRecordingChatId;
            oldListeningChatIds = newListeningChatIds;
        }
    }

    private async Task SyncPlaybackState(CancellationToken cancellationToken)
    {
        using var dCancellationTask = cancellationToken.ToTask();
        var cancellationTask = dCancellationTask.Resource;

        var cExpectedPlaybackStateBase = await Computed
            .Capture(() => ChatUI.GetRealtimePlaybackState())
            .ConfigureAwait(false);
        var playbackState = ChatPlayers.ChatPlaybackState;

        var changes = cExpectedPlaybackStateBase.Changes(FixedDelayer.ZeroUnsafe, cancellationToken);
        await foreach (var cExpectedPlaybackState in changes.ConfigureAwait(false)) {
            var expectedPlaybackState = cExpectedPlaybackState.ValueOrDefault;
            while (cExpectedPlaybackState.IsConsistent()) {
                var playbackStateValue = playbackState.Value;
                if (playbackStateValue is null or RealtimeChatPlaybackState) {
                    if (!ReferenceEquals(playbackStateValue, expectedPlaybackState)) {
                        if (playbackStateValue is null && !InteractiveUI.IsInteractive.Value)
                            await InteractiveUI.Demand("audio playback").ConfigureAwait(false);
                        playbackState.Value = expectedPlaybackState;
                    }
                }

                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                await Task.WhenAny(
                        playbackState.Computed.WhenInvalidated(CancellationToken.None),
                        cExpectedPlaybackState.WhenInvalidated(CancellationToken.None),
                        cancellationTask)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        // ReSharper disable once FunctionNeverReturns
    }

    private async Task SyncRecordingState(CancellationToken cancellationToken)
    {
        var cRecordingChatId = await Computed
            .Capture(() => SyncRecordingStateImpl(cancellationToken))
            .ConfigureAwait(false);
        // Let's update it continuously -- solely for the side effects of GetRecordingChatId runs
        await cRecordingChatId.When(_ => false, FixedDelayer.ZeroUnsafe, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    protected virtual async Task<Symbol> SyncRecordingStateImpl(CancellationToken cancellationToken)
    {
        // This compute method creates dependencies & gets recomputed on changes by SyncRecordingState.
        // The result it returns doesn't have any value - it runs solely for its own side effects.

        var recordingChatId = await ChatUI.RecordingChatId().ConfigureAwait(false);
        var recordingChatIdChanged = recordingChatId != _lastRecordingChatId;
        _lastRecordingChatId = recordingChatId;

        var recorderState = await AudioRecorder.State.Use(cancellationToken).ConfigureAwait(false);
        var recorderChatId = recorderState?.ChatId ?? Symbol.Empty;
        var recorderChatIdChanged = recorderChatId != _lastRecorderChatId;
        _lastRecorderChatId = recorderChatId;

        if (recordingChatId == recorderChatId) {
            // The state is in sync
            if (recordingChatId.IsEmpty)
                return default;

            if (await IsRecordingLanguageChanged().ConfigureAwait(false))
                SyncRecorderState(); // We need to toggle the recording in this case
        } else if (recordingChatIdChanged) {
            // The recording was activated or deactivated
            SyncRecorderState();
            if (recordingChatId.IsEmpty)
                return default;

            // Update _lastLanguageId
            await IsRecordingLanguageChanged().ConfigureAwait(false);
            // Start recording = start realtime playback
            await ChatUI.SetListeningState(recordingChatId, true).ConfigureAwait(false);
        } else if (recorderChatIdChanged) {
            // Something stopped (or started?) the recorder
            await ChatUI.SetRecordingChatId(recorderChatId).ConfigureAwait(false);
        }
        return default;

        async ValueTask<bool> IsRecordingLanguageChanged()
        {
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
        Symbol chatIdToStartRecording,
        CancellationToken cancellationToken)
        => BackgroundTask.Run(async () => {
                if (mustStop) {
                    // Recording is running - let's top it first;
                    await AudioRecorder.StopRecording(cancellationToken).ConfigureAwait(false);
                }
                if (!chatIdToStartRecording.IsEmpty) {
                    // And start the recording if we must
                    if (!InteractiveUI.IsInteractive.Value)
                        await InteractiveUI.Demand("audio recording").ConfigureAwait(false);
                    await AudioRecorder.StartRecording(chatIdToStartRecording, cancellationToken).ConfigureAwait(false);
                }
            },
            Log,
            "Failed to apply new recording state.",
            CancellationToken.None);

    private async Task SyncKeepAwakeState(CancellationToken cancellationToken)
    {
        var lastMustKeepAwake = false;
        var cMustKeepAwake0 = await Computed
            .Capture(() => ChatUI.MustKeepAwake())
            .ConfigureAwait(false);

        var changes = cMustKeepAwake0.Changes(FixedDelayer.Get(1), cancellationToken);
        await foreach (var cMustKeepAwake in changes.ConfigureAwait(false)) {
            var mustKeepAwake = cMustKeepAwake.Value;
            if (mustKeepAwake != lastMustKeepAwake) {
                // TODO(AY): Send this update to JS
                await KeepAwakeUI.SetKeepAwake(mustKeepAwake);
                lastMustKeepAwake = mustKeepAwake;
            }
        }
    }

    /// <summary>
    /// Monitors for inactivity for amount of time defined in ChatSettings.TurnOffRecordingAfterIdleTimeout.
    /// If no speech was transcribed from recording during this period the recording stops automatically.
    /// </summary>
    private async Task StopRecordingWhenInactive(CancellationToken cancellationToken)
    {
        var cLastChatEntry = await Computed
            .Capture(() => GetLastRecordingChatEntryInfo(cancellationToken))
            .ConfigureAwait(false);
        var lastChatEntry = (Symbol.Empty, 0L);

        while (!cancellationToken.IsCancellationRequested) {
            // Wait for recording started
            cLastChatEntry = await cLastChatEntry.When(x => !x.ChatId.IsEmpty, cancellationToken).ConfigureAwait(false);

            using var timeoutCts = new CancellationTokenSource(AudioSettings.IdleRecordingTimeout);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try {
                var toCompare = lastChatEntry;
                cLastChatEntry = await cLastChatEntry.When(x => toCompare != x, cts.Token).ConfigureAwait(false);
                lastChatEntry = cLastChatEntry.Value;
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested) {
                await UpdateRecorderState(true, Symbol.Empty, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    [ComputeMethod]
    protected virtual async Task<(Symbol ChatId, long LastEntryId)> GetLastRecordingChatEntryInfo(
        CancellationToken cancellationToken)
    {
        var recordingChatId = await ChatUI.RecordingChatId().ConfigureAwait(false);
        if (recordingChatId.IsEmpty)
            return (recordingChatId, 0);

        var (_, end) = await Chats
            .GetIdRange(Session, recordingChatId, ChatEntryType.Text, cancellationToken)
            .ConfigureAwait(false);
        return (recordingChatId, end);
    }

    private async Task ResetHighlightedChatEntry(CancellationToken cancellationToken)
    {
        var changes = ChatUI.HighlightedChatEntryId
            .Changes(FixedDelayer.ZeroUnsafe, cancellationToken)
            .Where(x => x.Value != 0);
        CancellationTokenSource? cts = null;
        try {
            await foreach (var cHighlightedChatEntryId in changes.ConfigureAwait(false)) {
                cts.CancelAndDisposeSilently();
                cts = cancellationToken.CreateLinkedTokenSource();
                var ctsToken = cts.Token;
                var highlightedChatEntryId = cHighlightedChatEntryId.Value;
                _ = BackgroundTask.Run(async () => {
                    await Clocks.UIClock.Delay(TimeSpan.FromSeconds(2), ctsToken).ConfigureAwait(false);
                    ChatUI.HighlightedChatEntryId.Set(
                        highlightedChatEntryId,
                        (expected, result) => result.IsValue(out var v) && v == expected ? 0 : result);
                }, CancellationToken.None);
            }
        }
        finally {
            cts.CancelAndDisposeSilently();
        }
    }
}
