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
        var playbackState = ChatPlayers.ChatPlaybackState;
        var cRealtimePlaybackState = await Computed
            .Capture(ct => ChatUI.GetRealtimeChatPlaybackState(true, ct), cancellationToken)
            .ConfigureAwait(false);

        while (true) {
            await playbackState
                .When(p => p is RealtimeChatPlaybackState { IsPlayingPinned: true }, cancellationToken)
                .ConfigureAwait(false);

            var doneTask = playbackState
                .When(p => p is not RealtimeChatPlaybackState { IsPlayingPinned: true }, cancellationToken);
            while (true) {
                if (!cRealtimePlaybackState.IsConsistent())
                    cRealtimePlaybackState = await cRealtimePlaybackState.Update(cancellationToken).ConfigureAwait(false);
                if (playbackState.Value is not RealtimeChatPlaybackState { IsPlayingPinned: true } rcps)
                    break;
                playbackState.Value = cRealtimePlaybackState.Value ?? rcps;
                var invalidatedTask = cRealtimePlaybackState.WhenInvalidated(cancellationToken);
                var completedTask = await Task.WhenAny(invalidatedTask, doneTask).ConfigureAwait(false);
                if (completedTask == doneTask)
                    break;
            }
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

        var settings = await ChatUserSettings.Get(Session, recordingChatId, cancellationToken).ConfigureAwait(false);
        var languageId = settings.LanguageOrDefault();
        var isLanguageChanged = _lastLanguageId.HasValue && languageId != _lastLanguageId;
        _lastLanguageId = languageId;

        var recorderState = await AudioRecorder.State.Use(cancellationToken).ConfigureAwait(false);
        var recorderChatId = recorderState?.ChatId ?? Symbol.Empty;
        var recorderChatIdChanged = recorderChatId != _lastRecorderChatId;
        _lastRecorderChatId = recorderChatId;

        if (recordingChatId == recorderChatId) {
            // The state is in sync
            if (isLanguageChanged && !recordingChatId.IsEmpty)
                SyncRecorderState(); // We need to toggle the recording in this case
        } else if (recordingChatIdChanged) {
            // The recording was activated or deactivates
            SyncRecorderState();
            if (!recordingChatId.IsEmpty) // Start recording = start realtime playback
                ChatPlayers.StartRealtimePlayback(false);
        } else if (recorderChatIdChanged) {
            // Something stopped (or started?) the recorder
            ChatUI.RecordingChatId.Value = recordingChatId = recorderChatId;
        }
        return recordingChatId;

        void SyncRecorderState() =>
            BackgroundTask.Run(async () => {
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
