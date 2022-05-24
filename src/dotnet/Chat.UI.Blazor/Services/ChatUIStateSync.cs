using ActualChat.Audio;
using ActualChat.Audio.UI.Blazor.Components;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatUIStateSync : WorkerBase
{
    // All properties are resolved in lazy fashion because otherwise we'll get a dependency cycle
    private ILogger? _log;
    private ChatUI? _chatUI;
    private ChatPlayers? _chatPlayers;
    private AudioRecorder? _audioRecorder;
    private IChatUserSettings? _chatUserSettings;
    private IChats? _chats;
    private AudioSettings? _chatSettings;

    private LanguageId? _lastLanguageId;
    private Symbol _lastRecordingChatId;
    private Symbol _lastRecorderChatId;

    private IServiceProvider Services { get; }
    private Session Session { get; }
    private ILogger Log => _log ??= Services.LogFor(GetType());
    private ChatUI ChatUI => _chatUI ??= Services.GetRequiredService<ChatUI>();
    private ChatPlayers ChatPlayers => _chatPlayers ??= Services.GetRequiredService<ChatPlayers>();
    private AudioRecorder AudioRecorder => _audioRecorder ??= Services.GetRequiredService<AudioRecorder>();
    private IChatUserSettings ChatUserSettings => _chatUserSettings ??= Services.GetRequiredService<IChatUserSettings>();
    private IChats Chats => _chats ??= Services.GetRequiredService<IChats>();
    private AudioSettings ChatSettings => _chatSettings ??= Services.GetRequiredService<AudioSettings>();

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
            StopRecordingWhenInactive(cancellationToken));

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
                if (!ReferenceEquals(playbackStateValue, expectedPlaybackState))
                    playbackState.Value = expectedPlaybackState;
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            await Task.WhenAny(
                playbackState.Computed.WhenInvalidated(cancellationToken),
                cExpectedPlaybackState.WhenInvalidated(cancellationToken))
                .ConfigureAwait(false);
        }
        // ReSharper disable once FunctionNeverReturns
    }

    private async Task SyncRecordingState(CancellationToken cancellationToken)
    {
        var cRecordingChatId = await Computed.Capture(GetRecordingChatId, cancellationToken).ConfigureAwait(false);
        // Let's update it continuously -- solely for the side effects of GetRecordingChatId runs
        await cRecordingChatId.When(_ => false, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    // TODO: why does computed method update state???
    protected virtual async Task<Symbol> GetRecordingChatId(CancellationToken cancellationToken)
    {
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
                ChatUI.IsPlaying.Value = true;
            }
        } else if (recorderChatIdChanged) {
            // Something stopped (or started?) the recorder
            ChatUI.RecordingChatId.Value = recordingChatId = recorderChatId;
        }
        return recordingChatId;

        async ValueTask<bool> IsLanguageChanged()
        {
            var settings = await ChatUserSettings.Get(Session, recordingChatId, cancellationToken).ConfigureAwait(false);
            var languageId = settings.LanguageOrDefault();
            var isLanguageChanged = _lastLanguageId.HasValue && languageId != _lastLanguageId;
            _lastLanguageId = languageId;
            return isLanguageChanged;
        }

        Task SyncRecorderState() => RestartRecording(recorderState != null, recordingChatId, cancellationToken);
    }

    [ComputeMethod]
    protected virtual async Task<(Symbol ChatId, long LastEntryId)> GetLastChatEntry(CancellationToken cancellationToken)
    {
        var recordingChatId = await ChatUI.RecordingChatId.Use(cancellationToken).ConfigureAwait(false);
        if (recordingChatId.IsEmpty)
            return (recordingChatId, 0);

        var (_, end) = await Chats.GetIdRange(Session, recordingChatId, ChatEntryType.Text, cancellationToken).ConfigureAwait(false);
        return (recordingChatId, end);
    }

    private Task RestartRecording(
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
                    await AudioRecorder.StartRecording(chatIdToStartRecording, cancellationToken).WhenCompleted.ConfigureAwait(false);
                }
            },
            Log,
            "Failed to apply new recording state.",
            CancellationToken.None);

    /// <summary>
    /// Monitors for inactivity for amount of time defined in ChatSettings.TurnOffRecordingAfterIdleTimeout.
    /// If no speech was transcribed from recording during this period the recording stops automatically.
    /// </summary>
    private async Task StopRecordingWhenInactive(CancellationToken cancellationToken)
    {
        var cLastChatEntry = await Computed.Capture(GetLastChatEntry, cancellationToken).ConfigureAwait(false);
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
                await RestartRecording(true, Symbol.Empty, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
