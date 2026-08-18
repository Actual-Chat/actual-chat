using ActualChat.Diagnostics;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using ActualLab.Diagnostics;
using ActualLab.Locking;

namespace ActualChat.UI.Blazor.App.Components;

public sealed class AudioRecorder : ProcessorBase, IAudioRecorderBackend
{
    public static readonly TimeSpan StartRecordingTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan StopRecordingTimeout = TimeSpan.FromSeconds(3);
    private static bool DebugMode => Constants.DebugMode.AudioRecording;

    private readonly AsyncLock _stateLock = new(LockReentryMode.CheckedPass);
    private readonly MutableState<AudioRecorderState> _state;
    private readonly IAudioRecorderEngine _engine;

    private Activity? _recordingActivity;
    private readonly AudioFocusRequester _audioFocusRequester;
    private AudioFocusScope? _audioFocusScope;

    private AppUIHub Hub { get; }
    private AudioFocusUI AudioFocusUI => Hub.AudioFocusUI;
    private TuneUI TuneUI => Hub.TuneUI;
    private AnalyticEvents AnalyticEvents => Hub.AnalyticEvents;
    private MomentClockSet Clocks => Hub.Clocks;

    private ILogger Log => field ??= Hub.LogFor(GetType());
    private ILogger? DebugLog => DebugMode ? Log : null;

    public MicrophonePermissionHandler MicrophonePermission
        => field ??= Hub.Services.GetRequiredService<MicrophonePermissionHandler>();

    public IState<AudioRecorderState> State => _state;

    public AudioRecorder(AppUIHub hub)
    {
        Hub = hub;
        _state = Hub.StateFactory.NewMutable(
            AudioRecorderState.Idle,
            StateCategories.Get(GetType(), nameof(State)));
        _engine = Hub.Services.GetRequiredService<IAudioRecorderEngine>();
        _audioFocusRequester = new AudioFocusRequester(AudioFocusMode.Recording, OnAudioFocusLost);
    }

    protected override async Task DisposeAsyncCore()
    {
        using var cts = new CancellationTokenSource(CoreConstants.DisposeTimeout);
        try {
            using var releaser = await _stateLock.Lock(cts.Token).ConfigureAwait(false);
            releaser.MarkLockedLocally();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) {
            Log.LogWarning(
                "{Type}: state lock wasn't released in {Timeout}; proceeding without it",
                GetType().GetName(), CoreConstants.DisposeTimeout);
        }
    }

    public async Task StartRecording(
        ChatId chatId,
        ChatEntryId? repliedChatEntryId,
        CancellationToken cancellationToken = default)
    {
        // Both waits are bounded and proceed anyway: neither is worth losing an utterance over,
        // and unbounded they burn the whole StartRecordingTimeout before the mic is even opened.
        var audioInitializer = Hub.AudioInitializer;
        try {
            await audioInitializer.WhenInitialized
                .WaitAsync(Constants.Audio.RecorderStartupWaitTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException) {
            Log.LogWarning(nameof(StartRecording) + ": audio initialization is still pending, starting anyway");
        }

        // The offset stamps frames; it isn't needed to open the microphone. Every device sleep
        // trips the suspend-drift gate, so awaiting a re-sync here delayed every gesture.
        var serverTimeSync = Hub.Services.GetService<ServerTimeSync>();
        if (serverTimeSync != null) {
            if (serverTimeSync.SyncCount > 0) {
                _ = BackgroundTask.Run(
                    () => serverTimeSync.EnsureSynced(CancellationToken.None),
                    Log, "Background server clock re-sync failed", CancellationToken.None);
            }
            else {
                // Never synced: there's no offset to stamp with, so this one has to wait.
                try {
                    await serverTimeSync.EnsureSynced(cancellationToken)
                        .WaitAsync(Constants.Audio.RecorderStartupWaitTimeout, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (TimeoutException) {
                    Log.LogWarning(nameof(StartRecording) + ": the server clock isn't synced yet, starting anyway");
                }
            }
        }

        using var releaser = await _stateLock.Lock(cancellationToken).ConfigureAwait(false);
        releaser.MarkLockedLocally();

        var state = State.Value;
        if (state.ChatId == chatId) {
            if (state.IsRecording)
                return; // Already started
        }
        else if (state.ChatId is not null)
            await StopRecordingUnsafe();

        MarkStarting(chatId);
        try {
            _audioFocusScope = await AudioFocusUI.TryAcquire(_audioFocusRequester).ConfigureAwait(false);
            if (_audioFocusScope is null)
                Log.LogWarning("Failed to gain audio focus for recording. Continue without it");

            var isStarted = await _engine.Start(chatId, repliedChatEntryId, cancellationToken).WaitAsync(StartRecordingTimeout, cancellationToken).ConfigureAwait(false);
            if (!isStarted) {
                MicrophonePermission.ForgetCached();
                Log.LogWarning(nameof(StartRecording) + ": chat #{ChatId} - can't access the microphone", chatId);
                // Cancel recording
                MarkStopped();
                throw new AudioRecorderException(
                    "Can't access the microphone - please check if the microphone access permission is granted.");
            }
        }
        catch (Exception e) when (e is not AudioRecorderException) {
            if (e is OperationCanceledException)
                // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
                DebugLog?.LogDebug($"{nameof(StartRecording)} is cancelled");
            else
                // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
                Log.LogError(e,$"{nameof(StartRecording)} failed");

            await StopRecordingUnsafe().ConfigureAwait(false);

            if (e is OperationCanceledException)
                throw;
            if (e is TimeoutException)
                throw new AudioRecorderException("Failed to start the recording in time.", e);
            throw new AudioRecorderException("Failed to start the recording.", e);
        }
    }

    public async Task<bool> StopRecording(CancellationToken cancellationToken = default)
    {
        using var releaser = await _stateLock.Lock(cancellationToken).ConfigureAwait(false);
        releaser.MarkLockedLocally();

        return await StopRecordingUnsafe().ConfigureAwait(false);
    }

    public async ValueTask ConversationSignal(CancellationToken cancellationToken)
        => await _engine.ConversationSignal(cancellationToken).ConfigureAwait(false);

    public async Task<AudioDiagnosticsState> RunDiagnostics(CancellationToken cancellationToken)
        => await _engine.RunDiagnostics(cancellationToken).ConfigureAwait(false);

    // JS backend callback handlers
    [JSInvokable]
    public bool IsRecording(string chatId)
    {
        var state = State.Value;
        if (!string.Equals(state.ChatId?.Value, chatId, StringComparison.OrdinalIgnoreCase))
            return false; // Not recording

        return state.IsRecording;
    }

    // JS backend callback handlers
    [JSInvokable]
    public void OnRecordingStateChange(bool isRecording, bool isSignalDetected, bool isConnected, bool isVoiceActive)
    {
        // Log.LogInformation(
        //     "OnRecordingStateChange: isRecording={IsRecording}, isSignalDetected={IsSignalDetected}, isConnected={IsConnected}, isVoiceActive={IsVoiceActive}",
        //     isRecording,
        //     isSignalDetected,
        //     isConnected,
        //     isVoiceActive);
        var state = State.Value;
        if (state.ChatId is null) {
            if (isRecording)
                throw StandardError.Internal(
                    "Something is off: OnRecordingStateChange() is called with active microphone, but ChatId.IsNone == true.");

            isVoiceActive = false;
        }

        var newState = state with {
            IsRecording = isRecording,
            IsSignalDetected = isSignalDetected,
            IsConnected = isConnected,
            IsVoiceActive = isVoiceActive,
        };
        var recordingHasStarted = isRecording && !state.IsRecording;
        var recordingHasCompleted = !isRecording && state.IsRecording;
        var recordingDuration = TimeSpan.Zero;
        if (recordingHasStarted) {
            newState = newState with { RecordingStartTime = Clocks.SystemClock.Now };
            _ = TuneUI.Play(Tune.ConfirmRecording);
        }
        else if (recordingHasCompleted) {
            recordingDuration = Clocks.SystemClock.Now - newState.RecordingStartTime;
            newState = newState with { RecordingStartTime = Moment.EpochStart };
        }
        if (state != newState)
            UpdateState(newState);
        _recordingActivity
            ?.AddSentrySimulatedEvent(new ActivityEvent("Recording state changed",
                tags: new ActivityTagsCollection {
                    { "AC." + nameof(AudioRecorderState.IsRecording), isRecording },
                    { "AC." + nameof(AudioRecorderState.IsSignalDetected), isSignalDetected },
                    { "AC." + nameof(AudioRecorderState.IsConnected), isConnected },
                    { "AC." + nameof(AudioRecorderState.IsVoiceActive), isVoiceActive },
                }));
        if (recordingHasStarted)
            AnalyticEvents.RaiseRecordingStarted();
        else if (recordingHasCompleted)
            AnalyticEvents.RaiseRecordingCompleted((int)recordingDuration.TotalMilliseconds);
        DebugLog?.LogDebug("Chat #{ChatId}: recording state changed: {State}", state.ChatId, state);
    }

    // Private methods

    private void UpdateState(AudioRecorderState state)
        => _state.Value = state;

    private async Task<bool> StopRecordingUnsafe()
    {
        var chatId = State.Value.ChatId;
        if (chatId is null)
            return true; // Nothing to do

        // This method should reliably stop the recording, so we don't use normal cancellation here
        try {
            await _engine.Stop(CancellationToken.None).WaitAsync(StopRecordingTimeout).ConfigureAwait(false);
        }
        catch (JSDisconnectedException) { } // Circuit is disposed or disposing
        catch (ObjectDisposedException) { } // Circuit is disposed or disposing
        catch (Exception e) {
            var reason = e is TimeoutException ? "timed out" : "failed";
            // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
            Log.LogError(e, $"{nameof(StopRecordingUnsafe)}: chat #{{ChatId}} - {reason}, recorder state is in doubt", chatId);
            return false;
        }
        MarkStopped();
        return true;
    }

    // MarkXxx

    private void MarkStarting(ChatId chatId)
    {
        var currentState = State.Value;
        var (_, isRecording, isSignalDetected, isConnected, isVoiceActive) = currentState;
        UpdateState(new AudioRecorderState(chatId) {
            IsRecording = isRecording,
            IsSignalDetected = isSignalDetected,
            IsConnected = isConnected,
            IsVoiceActive = isVoiceActive,
            RecordingStartTime = currentState.RecordingStartTime,
        });
        // ReSharper disable once ExplicitCallerInfoArgument
        _recordingActivity = AppUIInstruments.ActivitySource.StartActivity(GetType(), "Record");
        _recordingActivity
            ?.SetTag("AC." + nameof(ChatId), chatId.Value)
            .AddSentrySimulatedEvent(new ActivityEvent("Recoding is starting",
                tags: new ActivityTagsCollection {
                    { "AC." + nameof(AudioRecorderState.IsRecording), isRecording },
                    { "AC." + nameof(AudioRecorderState.IsSignalDetected), isSignalDetected },
                    { "AC." + nameof(AudioRecorderState.IsConnected), isConnected },
                    { "AC." + nameof(AudioRecorderState.IsVoiceActive), isVoiceActive },
                }));
        DebugLog?.LogDebug("Chat #{ChatId}: recording is starting, {State}", chatId, State.Value);
    }

    private void MarkStopped()
    {
        var currentState = State.Value;
        var (_, isRecording, isSignalDetected, isConnected, isVoiceActive) = currentState;
        UpdateState(new AudioRecorderState(null) {
            IsRecording = isRecording,
            IsSignalDetected = isSignalDetected,
            IsConnected = isConnected,
            IsVoiceActive = isVoiceActive,
            RecordingStartTime = currentState.RecordingStartTime,
        });
        _recordingActivity
            ?.AddSentrySimulatedEvent(new ActivityEvent("Recording is stopped",
                tags: new ActivityTagsCollection {
                    { "AC." + nameof(AudioRecorderState.IsRecording), isRecording },
                    { "AC." + nameof(AudioRecorderState.IsSignalDetected), isSignalDetected },
                    { "AC." + nameof(AudioRecorderState.IsConnected), isConnected },
                    { "AC." + nameof(AudioRecorderState.IsVoiceActive), isVoiceActive },
                }));
        _recordingActivity?.Dispose();
        ReleaseAudioFocus();
        DebugLog?.LogDebug("Recording is stopped, {State}", State.Value);
    }

    private void ReleaseAudioFocus()
    {
        _audioFocusScope?.Dispose();
        _audioFocusScope = null;
    }

    private AudioFocusRestoreHandler? OnAudioFocusLost(bool mayRecover, bool canDuck)
        // Do nothing even app lost the audio focus.
        => null;

    public class AudioDiagnosticsState
    {
        public bool? IsPlayerInitialized { get; init; }
        public bool? IsRecorderInitialized { get; init; }
        public bool? HasMicrophonePermission { get; init; }
        public bool? IsAudioContextSourceMaintained { get; init; }
        public bool? IsAudioContextRunning { get; init; }
        public bool? HasMicrophoneStream { get; init; }
        public bool? IsVadActive { get; init; }
        public VadEvent? LastVadEvent { get; init; }
        public long? LastVadFrameProcessedAt { get; init; }
        public bool? IsConnected { get; init; }
        public bool? IsSignalDetected { get; init; }
        public long? LastFrameProcessedAt { get; init; }
        public string? VadWorkletState { get; init; }
        public long? LastVadWorkletFrameProcessedAt { get; init; }
        public string? EncoderWorkletState { get; init; }
        public long? LastEncoderWorkletFrameProcessedAt { get; init; }

        public override string ToString()
            => $"{nameof(AudioDiagnosticsState)} {{ {nameof(IsPlayerInitialized)}: {IsPlayerInitialized}, {nameof(IsRecorderInitialized)}: {IsRecorderInitialized}, {nameof(HasMicrophonePermission)}: {HasMicrophonePermission}, {nameof(IsAudioContextSourceMaintained)}: {IsAudioContextSourceMaintained}, {nameof(IsAudioContextRunning)}: {IsAudioContextRunning}, {nameof(HasMicrophoneStream)}: {HasMicrophoneStream}, {nameof(IsVadActive)}: {IsVadActive}, {nameof(LastVadEvent)}: {LastVadEvent}, {nameof(LastVadFrameProcessedAt)}: {LastVadFrameProcessedAt}, {nameof(IsConnected)}: {IsConnected}, {nameof(IsSignalDetected)}: {IsSignalDetected}, {nameof(LastFrameProcessedAt)}: {LastFrameProcessedAt}, {nameof(VadWorkletState)}: {VadWorkletState}, {nameof(LastVadWorkletFrameProcessedAt)}: {LastVadWorkletFrameProcessedAt}, {nameof(EncoderWorkletState)}: {EncoderWorkletState}, {nameof(LastEncoderWorkletFrameProcessedAt)}: {LastEncoderWorkletFrameProcessedAt} }}";
    }

    public class VadEvent
    {
        public string? Kind { get; init; }
        public double Offset { get; init; }
        public double Duration { get; init; }
        public double SpeechProb { get; init; }

        public override string ToString()
            => $"{nameof(VadEvent)} {{ {nameof(Kind)}: {Kind}, {nameof(Offset)}: {Offset}, {nameof(Duration)}: {Duration}, {nameof(SpeechProb)}: {SpeechProb} }}";
    }
}
