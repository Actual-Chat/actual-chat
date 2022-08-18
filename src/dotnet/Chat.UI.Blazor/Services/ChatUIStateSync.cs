using ActualChat.Audio;
using ActualChat.Audio.UI.Blazor.Components;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatUIStateSync : WorkerBase
{
    // All properties are resolved in lazy fashion because otherwise we'll get a dependency cycle
    private ILogger? _log;
    private ChatPlayers? _chatPlayers;
    private IChats? _chats;
    private IChatUserSettings? _chatUserSettings;
    private AudioRecorder? _audioRecorder;
    private AudioSettings? _chatSettings;
    private KeepAwakeUI? _keepAwakeUI;
    private ChatUI? _chatUI;
    private UserInteractionUI? _userInteractionUI;
    private IJSRuntime? _js;

    private LanguageId? _lastLanguageId;
    private Symbol _lastRecordingChatId;
    private Symbol _lastRecorderChatId;

    private IServiceProvider Services { get; }
    private Session Session { get; }
    private ILogger Log => _log ??= Services.LogFor(GetType());
    private ChatPlayers ChatPlayers => _chatPlayers ??= Services.GetRequiredService<ChatPlayers>();
    private IChats Chats => _chats ??= Services.GetRequiredService<IChats>();
    private IChatUserSettings ChatUserSettings => _chatUserSettings ??= Services.GetRequiredService<IChatUserSettings>();
    private AudioRecorder AudioRecorder => _audioRecorder ??= Services.GetRequiredService<AudioRecorder>();
    private AudioSettings ChatSettings => _chatSettings ??= Services.GetRequiredService<AudioSettings>();
    private KeepAwakeUI KeepAwakeUI => _keepAwakeUI ??= Services.GetRequiredService<KeepAwakeUI>();
    private ChatUI ChatUI => _chatUI ??= Services.GetRequiredService<ChatUI>();
    private UserInteractionUI UserInteractionUI => _userInteractionUI ??= Services.GetRequiredService<UserInteractionUI>();
    private IJSRuntime JS => _js ??= Services.GetRequiredService<IJSRuntime>();

    public ChatUIStateSync(Session session, IServiceProvider services)
    {
        Session = session;
        Services = services;
    }

    // Protected methods

    protected override Task RunInternal(CancellationToken cancellationToken)
        => Task.WhenAll(
            SyncPlaybackState(cancellationToken),
            SyncRecordingState(cancellationToken),
            SyncKeepAwakeState(cancellationToken),
            StopRecordingWhenInactive(cancellationToken),
            ResetHighlightedChatEntry(cancellationToken));

    private async Task SyncPlaybackState(CancellationToken cancellationToken)
    {
        var cExpectedPlaybackState = await Computed
            .Capture(ct => ChatUI.GetRealtimeChatPlaybackState(ct), cancellationToken)
            .ConfigureAwait(false);
        var playbackState = ChatPlayers.ChatPlaybackState;

        while (true) {
            if (!cExpectedPlaybackState.IsConsistent())
                cExpectedPlaybackState = await cExpectedPlaybackState.Update(cancellationToken).ConfigureAwait(false);
            var expectedPlaybackState = cExpectedPlaybackState.ValueOrDefault;

            var playbackStateValue = playbackState.Value;
            if (playbackStateValue is null or RealtimeChatPlaybackState) {
                if (!ReferenceEquals(playbackStateValue, expectedPlaybackState)) {
                    if (playbackStateValue is not null && !UserInteractionUI.IsInteractionHappened.Value)
                        await UserInteractionUI.RequestInteraction("audio playback").ConfigureAwait(false);
                    playbackState.Value = expectedPlaybackState;
                }
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            var completedTask = await Task.WhenAny(
                playbackState.Computed.WhenInvalidated(cancellationToken),
                cExpectedPlaybackState.WhenInvalidated(cancellationToken))
                .ConfigureAwait(false);
#pragma warning disable MA0004
            await completedTask; // Will throw an exception on cancellation
#pragma warning restore MA0004
        }
        // ReSharper disable once FunctionNeverReturns
    }

    private async Task SyncRecordingState(CancellationToken cancellationToken)
    {
        var cRecordingChatId = await Computed.Capture(SyncRecordingStateImpl, cancellationToken).ConfigureAwait(false);
        // Let's update it continuously -- solely for the side effects of GetRecordingChatId runs
        await cRecordingChatId.When(_ => false, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    protected virtual async Task<Symbol> SyncRecordingStateImpl(CancellationToken cancellationToken)
    {
        // This compute method creates dependencies & gets recomputed on changes by SyncRecordingState.
        // The result it returns doesn't have any value - it runs solely for its own side effects.

        var recordingChatId = await ChatUI.RecordingChatId.Use(cancellationToken).ConfigureAwait(false);
        var recordingChatIdChanged = recordingChatId != _lastRecordingChatId;
        _lastRecordingChatId = recordingChatId;

        var recorderState = await AudioRecorder.State.Use(cancellationToken).ConfigureAwait(false);
        var recorderChatId = recorderState?.ChatId ?? Symbol.Empty;
        var recorderChatIdChanged = recorderChatId != _lastRecorderChatId;
        _lastRecorderChatId = recorderChatId;

        if (recordingChatId == recorderChatId) {
            // The state is in sync
            if (!recordingChatId.IsEmpty) {
                if (await IsLanguageChanged().ConfigureAwait(false))
                    await SyncRecorderState().ConfigureAwait(false); // We need to toggle the recording in this case
            }
        } else if (recordingChatIdChanged) {
            // The recording was activated or deactivated
            await SyncRecorderState().ConfigureAwait(false);
            if (!recordingChatId.IsEmpty) {
                // Update _lastLanguageId
                await IsLanguageChanged().ConfigureAwait(false);
                // Start recording = start realtime playback
                ChatUI.SetListeningState(recordingChatId, true);
            }
        } else if (recorderChatIdChanged) {
            // Something stopped (or started?) the recorder
            ChatUI.RecordingChatId.Value = recorderChatId;
        }
        return default;

        async ValueTask<bool> IsLanguageChanged()
        {
            var settings = await ChatUserSettings.Get(Session, recordingChatId, cancellationToken).ConfigureAwait(false);
            var languageId = settings.LanguageOrDefault();
            var isLanguageChanged = _lastLanguageId.HasValue && languageId != _lastLanguageId;
            _lastLanguageId = languageId;
            return isLanguageChanged;
        }

        Task SyncRecorderState()
            => UpdateRecorderState(recorderState != null, recordingChatId, cancellationToken);
    }

    private Task UpdateRecorderState(
        bool mustStop,
        Symbol chatIdToStartRecording,
        CancellationToken cancellationToken)
        => BackgroundTask.Run(async () => {
                if (mustStop) {
                    // Recording is running - let's top it first;
                    await AudioRecorder.StopRecording().WhenCompleted.ConfigureAwait(false);
                }
                if (!chatIdToStartRecording.IsEmpty) {
                    // And start the recording if we must
                    if (!UserInteractionUI.IsInteractionHappened.Value)
                        await UserInteractionUI.RequestInteraction("audio recording").ConfigureAwait(false);
                    await AudioRecorder.StartRecording(chatIdToStartRecording, cancellationToken).WhenCompleted.ConfigureAwait(false);
                }
            },
            Log,
            "Failed to apply new recording state.",
            CancellationToken.None);

    private async Task SyncKeepAwakeState(CancellationToken cancellationToken)
    {
        var lastMustKeepAwake = false;
        var cMustKeepAwake = await Computed.Capture(ChatUI.MustKeepAwake, cancellationToken).ConfigureAwait(false);
        // Let's update it continuously -- solely for the side effects of GetRecordingChatId runs
        await foreach (var cUpdate in cMustKeepAwake.Changes(cancellationToken).ConfigureAwait(false)) {
            var mustKeepAwake = cUpdate.Value;
            if (mustKeepAwake != lastMustKeepAwake) {
                // TODO(AY): Send this update to JS
                await KeepAwakeUI.SetKeepAwake(mustKeepAwake, cancellationToken);
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
        var cLastChatEntry = await Computed.Capture(GetLastRecordingChatEntryInfo, cancellationToken).ConfigureAwait(false);
        var lastChatEntry = (Symbol.Empty, 0L);

        while (!cancellationToken.IsCancellationRequested) {
            // wait for recording started
            cLastChatEntry = await cLastChatEntry.When(x => !x.ChatId.IsEmpty, cancellationToken: cancellationToken).ConfigureAwait(false);

            using var timeoutCts = new CancellationTokenSource(ChatSettings.IdleRecordingTimeout);
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
        var recordingChatId = await ChatUI.RecordingChatId.Use(cancellationToken).ConfigureAwait(false);
        if (recordingChatId.IsEmpty)
            return (recordingChatId, 0);

        var (_, end) = await Chats
            .GetIdRange(Session, recordingChatId, ChatEntryType.Text, cancellationToken)
            .ConfigureAwait(false);
        return (recordingChatId, end);
    }

    private async Task ResetHighlightedChatEntry(CancellationToken cancellationToken)
    {
        await foreach (var change in ChatUI.HighlightedChatEntryId.Changes(cancellationToken).Where(x => x.Value != 0).ConfigureAwait(false)) {
            using var timeoutCts = new CancellationTokenSource(2000);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try {
                await ChatUI.HighlightedChatEntryId.When(x => x != change.Value, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested) {
                ChatUI.HighlightedChatEntryId.Value = 0;
            }
        }
    }
}
