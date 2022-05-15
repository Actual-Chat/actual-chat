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

    public ChatUIStateSync(Session session, IServiceProvider services)
    {
        Session = session;
        Services = services;
    }

    // Protected methods

    protected override Task RunInternal(CancellationToken cancellationToken)
        => Task.WhenAll(
            SyncPlaybackState(cancellationToken),
            SyncRecordingState(cancellationToken));

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
            if (playbackStateValue == null) {
                if (expectedPlaybackState != null)
                    ChatPlayers.StartPlayback(expectedPlaybackState);
            }
            else if (playbackStateValue is RealtimeChatPlaybackState realtimePlaybackState) {
                if (realtimePlaybackState != expectedPlaybackState)
                    playbackState.Value = expectedPlaybackState;
            }

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
            // The recording was activated or deactivates
            await SyncRecorderState().ConfigureAwait(false);
            if (!recordingChatId.IsEmpty) {
                // Update _lastLanguageId
                await IsLanguageChanged().ConfigureAwait(false);
                // Start recording = start realtime playback
                ChatUI.IsPlayingActive.Value = true;
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

        Task SyncRecorderState()
            => BackgroundTask.Run(async () => {
                if (recorderState != null) {
                    // Recording is running - let's top it first;
                    var stopRecordingProcess = AudioRecorder.StopRecording();
                    await stopRecordingProcess.WhenCompleted.ConfigureAwait(false);
                }
                if (!recordingChatId.IsEmpty) {
                    // And start the recording if we must
                    var startRecordingProcess = AudioRecorder.StartRecording(recordingChatId, cancellationToken);
                    await startRecordingProcess.WhenCompleted.ConfigureAwait(false);
                }
            }, Log, "Failed to apply new recording state.", CancellationToken.None);
    }
}
