using ActualChat.Diagnostics;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using ActualLab.Diagnostics;
using ActualLab.Locking;

namespace ActualChat.UI.Blazor.App.Components;

public class AudioRecorder : ProcessorBase, IAudioRecorderBackend
{
    private static readonly TimeSpan StartRecordingTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan StopRecordingTimeout = TimeSpan.FromSeconds(3);
    private static bool DebugMode => Constants.DebugMode.AudioRecording;

    private readonly AsyncLock _stateLock = new(LockReentryMode.CheckedPass);
    private readonly MutableState<AudioRecorderState> _state;
    private readonly IAudioRecorderEngine _engine;
    private SessionTokens? _sessionTokens;

    private DotNetObjectReference<IAudioRecorderBackend>? _blazorRef;
    private Activity? _recordingActivity;

    private AppUIHub Hub { get; }
    private HostInfo HostInfo => Hub.HostInfo;
    private AnalyticEvents AnalyticEvents => Hub.AnalyticEvents;
    private MomentClockSet Clocks => Hub.Clocks;
    private IJSRuntime JS => Hub.JS;
    private TuneUI TuneUI => Hub.TuneUI;

    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= Hub.LogFor(GetType());
    private ILogger? DebugLog => DebugMode ? Log : null;

    [field: AllowNull, MaybeNull]
    public MicrophonePermissionHandler MicrophonePermission
        => field ??= Hub.Services.GetRequiredService<MicrophonePermissionHandler>();
    public IState<AudioRecorderState> State => _state;
    public Task WhenInitialized { get; }

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AudioRecorder))]
    public AudioRecorder(AppUIHub hub)
    {
        Hub = hub;
        _state = Hub.StateFactory.NewMutable(
            AudioRecorderState.Idle,
            StateCategories.Get(GetType(), nameof(State)));
        _engine = Hub.Services.GetRequiredService<IAudioRecorderEngine>();
        WhenInitialized = Initialize();
        return;

        async Task Initialize() {
            _blazorRef = DotNetObjectReference.Create<IAudioRecorderBackend>(this);
            await _engine.InitializeAsync(this).ConfigureAwait(false);
        }
    }

    protected override async Task DisposeAsyncCore()
    {
        using var releaser = await _stateLock.Lock().ConfigureAwait(false);
        releaser.MarkLockedLocally();

        if (_engine is JSRecorderEngine jsEngine) {
            // JS engine holds JS object; nothing to dispose here beyond engine lifetime
        }
        _blazorRef.DisposeSilently();
        _blazorRef = null!;
    }

    public async Task StartRecording(
        ChatId chatId,
        ChatEntryId? repliedChatEntryId,
        CancellationToken cancellationToken = default)
    {
        await WhenInitialized.WaitAsync(cancellationToken).ConfigureAwait(false);
        var audioInitializer = Hub.Services.GetRequiredService<AudioInitializer>();
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
            var isStarted = await _engine.StartAsync(chatId, repliedChatEntryId, sessionToken, cancellationToken).WaitAsync(StartRecordingTimeout, cancellationToken).ConfigureAwait(false);
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
        await WhenInitialized.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var releaser = await _stateLock.Lock(cancellationToken).ConfigureAwait(false);
        releaser.MarkLockedLocally();

        return await StopRecordingUnsafe().ConfigureAwait(false);
    }

    public async ValueTask EnsureConnected(bool quickReconnect, CancellationToken cancellationToken)
    {
        await WhenInitialized.WaitAsync(cancellationToken).ConfigureAwait(false);
        await _engine.EnsureConnected(quickReconnect, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ConversationSignal(CancellationToken cancellationToken)
    {
        await WhenInitialized.WaitAsync(cancellationToken).ConfigureAwait(false);
        await _engine.ConversationSignal(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AudioDiagnosticsState> RunDiagnostics(CancellationToken cancellationToken)
    {
        await WhenInitialized.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await _engine.RunDiagnostics(cancellationToken).ConfigureAwait(false);
    }

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
            await _engine.StopAsync(CancellationToken.None).WaitAsync(StopRecordingTimeout).ConfigureAwait(false);
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

    internal async Task<bool?> CheckPermission(CancellationToken cancellationToken = default)
    {
        await WhenInitialized.WaitAsync(cancellationToken).ConfigureAwait(false);
        var state = await _engine.CheckPermissionAsync(cancellationToken).ConfigureAwait(false);
        return state switch {
            "prompt" => null,
            "denied" => false,
            "granted" => true,
            _ => null,
        };
    }

    internal async Task<bool> RequestPermission(CancellationToken cancellationToken = default)
    {
        await WhenInitialized.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await _engine.RequestPermissionAsync(cancellationToken).ConfigureAwait(false);
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
            ?.SetTag("AC." + nameof(ChatId), chatId)
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
        DebugLog?.LogDebug("Recording is stopped, {State}", State.Value);
    }

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
