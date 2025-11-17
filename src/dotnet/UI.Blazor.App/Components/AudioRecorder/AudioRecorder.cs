using ActualChat.Diagnostics;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using ActualLab.Diagnostics;
using ActualLab.Locking;

namespace ActualChat.UI.Blazor.App.Components;

public class AudioRecorder : ProcessorBase, IAudioRecorderBackend
{
    public static readonly TimeSpan StartRecordingTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan StopRecordingTimeout = TimeSpan.FromSeconds(3);
    private static bool DebugMode => Constants.DebugMode.AudioRecording;

    private readonly AsyncLock _stateLock = new(LockReentryMode.CheckedPass);
    private readonly MutableState<AudioRecorderState> _state;
    private readonly IAudioRecorderEngine _engine;
    private SessionTokens? _sessionTokens;

    private Activity? _recordingActivity;
    private readonly AudioFocusConsumer _audioFocusConsumer;
    private IAudioFocusActivation? _audioFocusActivation;

    private AppUIHub Hub { get; }
    private HostInfo HostInfo => Hub.HostInfo;
    private AnalyticEvents AnalyticEvents => Hub.AnalyticEvents;
    private MomentClockSet Clocks => Hub.Clocks;
    private TuneUI TuneUI => Hub.TuneUI;

    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= Hub.LogFor(GetType());
    private ILogger? DebugLog => DebugMode ? Log : null;

    protected AudioFocusService AudioFocusService => Hub.AudioFocusService;
    protected AudioWidgetSession AudioWidgetSession => Hub.AudioWidgetSession;

    [field: AllowNull, MaybeNull]
    public MicrophonePermissionHandler MicrophonePermission
        => field ??= Hub.Services.GetRequiredService<MicrophonePermissionHandler>();

    public IState<AudioRecorderState> State => _state;

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AudioRecorder))]
    public AudioRecorder(AppUIHub hub)
    {
        Hub = hub;
        _state = Hub.StateFactory.NewMutable(
            AudioRecorderState.Idle,
            StateCategories.Get(GetType(), nameof(State)));
        _engine = Hub.Services.GetRequiredService<IAudioRecorderEngine>();
        _audioFocusConsumer = new AudioFocusConsumer(AudioMode.Recording, LostFocusCallback);
    }

    protected override async Task DisposeAsyncCore()
    {
        using var releaser = await _stateLock.Lock().ConfigureAwait(false);
        releaser.MarkLockedLocally();
    }

    public async Task StartRecording(
        ChatId chatId,
        ChatEntryId? repliedChatEntryId,
        CancellationToken cancellationToken = default)
    {
        var audioInitializer = Hub.AudioInitializer;
        await audioInitializer.WhenInitialized.ConfigureAwait(false);

        using var releaser = await _stateLock.Lock(cancellationToken).ConfigureAwait(false);
        releaser.MarkLockedLocally();

        var state = State.Value;
        if (state.ChatId == chatId) {
            if (state.IsRecording)
                return; // Already started
        }
        else if (state.ChatId is not null)
            await StopRecordingUnsafe();

        var sessionToken = "";
        if (HostInfo.HostKind.IsApp()) {
            var sessionTokens = _sessionTokens ??= Hub.Services.GetRequiredService<SessionTokens>();
            var secureToken = await sessionTokens.Get(cancellationToken).ConfigureAwait(false);
            sessionToken = secureToken.Token;
        }

        MarkStarting(chatId);
        try {
            _audioFocusActivation = await AudioFocusService.TryGainAudioFocus(_audioFocusConsumer).ConfigureAwait(false);
            if (_audioFocusActivation is null)
                Log.LogWarning("Failed to gain audio focus for recording. Continue without it");

            var isStarted = await _engine.Start(chatId, repliedChatEntryId, sessionToken, cancellationToken).WaitAsync(StartRecordingTimeout, cancellationToken).ConfigureAwait(false);
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

    public async ValueTask EnsureConnected(bool quickReconnect, CancellationToken cancellationToken)
        => await _engine.EnsureConnected(quickReconnect, cancellationToken).ConfigureAwait(false);

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
        AudioWidgetSession.OnRecodingStateChanged(chatId);
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
        AudioWidgetSession.OnRecodingStateChanged(null);
    }

    private void ReleaseAudioFocus()
    {
        _audioFocusActivation?.Release();
        _audioFocusActivation = null;
    }

    private RestoreFocusHandler? LostFocusCallback(bool mayRecover)
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
