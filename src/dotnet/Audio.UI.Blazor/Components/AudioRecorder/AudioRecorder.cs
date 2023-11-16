using ActualChat.Audio.UI.Blazor.Module;
using ActualChat.Audio.UI.Blazor.Services;
using ActualChat.Hosting;
using ActualChat.Permissions;
using ActualChat.UI.Blazor.Services;
using Stl.Locking;

namespace ActualChat.Audio.UI.Blazor.Components;

public class AudioRecorder : ProcessorBase, IAudioRecorderBackend
{
    private static readonly string JSCreateMethod = $"{AudioBlazorUIModule.ImportName}.AudioRecorder.create";
    private static readonly TimeSpan StartRecordingTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan StopRecordingTimeout = TimeSpan.FromSeconds(3);

    private readonly AsyncLock _stateLock = AsyncLock.New(LockReentryMode.Unchecked);
    private readonly IMutableState<AudioRecorderState> _state;
    private Activity? _recordingActivity;
    private IJSObjectReference _jsRef = null!;
    private SessionTokens? _sessionTokens;

    private ILogger Log { get; }
    private ILogger? DebugLog => DebugMode ? Log : null;
    private bool DebugMode => Constants.DebugMode.AudioRecording;
    private DotNetObjectReference<IAudioRecorderBackend>? _blazorRef;

    private HostInfo HostInfo { get; }
    private IJSRuntime JS { get; }
    private IServiceProvider Services { get; }
    private MomentClockSet Clocks { get; }

    public MicrophonePermissionHandler MicrophonePermission { get; }
    public IState<AudioRecorderState> State => _state;
    public Task WhenInitialized { get; }

    public AudioRecorder(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        Clocks = services.Clocks();
        HostInfo = services.GetRequiredService<HostInfo>();
        JS = services.JSRuntime();
        MicrophonePermission = services.GetRequiredService<MicrophonePermissionHandler>();

        _state = services.StateFactory().NewMutable(
            AudioRecorderState.Idle,
            StateCategories.Get(GetType(), nameof(State)));
        WhenInitialized = Initialize();
        return;

        async Task Initialize() {
            _blazorRef = DotNetObjectReference.Create<IAudioRecorderBackend>(this);
            _jsRef = await JS.InvokeAsync<IJSObjectReference>(JSCreateMethod, _blazorRef).ConfigureAwait(false);
        }
    }

    protected override async Task DisposeAsyncCore()
    {
        using var _ = await _stateLock.Lock().ConfigureAwait(false);
        await _jsRef.DisposeSilentlyAsync("dispose").ConfigureAwait(false);
        _blazorRef.DisposeSilently();
        _jsRef = null!;
        _blazorRef = null!;
    }

    public async Task StartRecording(
        ChatId chatId,
        ChatEntryId repliedChatEntryId,
        CancellationToken cancellationToken = default)
    {
        if (chatId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(chatId));

        await WhenInitialized.WaitAsync(cancellationToken).ConfigureAwait(false);
        var audioInitializer = Services.GetRequiredService<AudioInitializer>();
        await audioInitializer.WhenInitialized.ConfigureAwait(false);

        using var _ = await _stateLock.Lock(cancellationToken).ConfigureAwait(false);
        var state = _state.Value;
        if (state.ChatId == chatId) {
            if (state.IsRecording)
                return; // Already started
        }
        else if (!state.ChatId.IsNone)
            await StopRecordingUnsafe();

        var sessionToken = "";
        if (HostInfo.AppKind.IsClient()) {
            var sessionTokens = _sessionTokens ??= Services.GetRequiredService<SessionTokens>();
            var secureToken = await sessionTokens.Get(cancellationToken).ConfigureAwait(false);
            sessionToken = secureToken.Token;
        }

        MarkStarting(chatId);
        try {
            var isStarted = await _jsRef
                .InvokeAsync<bool>("startRecording", CancellationToken.None, chatId, repliedChatEntryId, sessionToken)
                .AsTask().WaitAsync(StartRecordingTimeout, cancellationToken).ConfigureAwait(false);
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
                DebugLog?.LogDebug($"{StartRecording} is cancelled");
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
        using var _ = await _stateLock.Lock(cancellationToken).ConfigureAwait(false);
        return await StopRecordingUnsafe().ConfigureAwait(false);
    }

    public async ValueTask Reconnect(CancellationToken cancellationToken)
    {
        await WhenInitialized.WaitAsync(cancellationToken).ConfigureAwait(false);
        await _jsRef.InvokeVoidAsync("reconnect", CancellationToken.None)
            .AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ConversationSignal(CancellationToken cancellationToken)
    {
        await WhenInitialized.WaitAsync(cancellationToken).ConfigureAwait(false);
        await _jsRef.InvokeVoidAsync("conversationSignal", CancellationToken.None)
            .AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AudioDiagnosticsState> RunDiagnostics(CancellationToken cancellationToken)
    {
        await WhenInitialized.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await _jsRef.InvokeAsync<AudioDiagnosticsState>("runDiagnostics", cancellationToken).ConfigureAwait(false);
    }

    // JS backend callback handlers
    [JSInvokable]
    public void OnRecordingStateChange(bool isRecording, bool isConnected, bool isVoiceActive)
    {
        var state = _state.Value;
        if (state.ChatId.IsNone) {
            if (isRecording)
                throw StandardError.Internal("Something is off: OnRecordingStateChange() is called with active microphone, but ChatId.IsNone == true.");

            isVoiceActive = false;
        }

        var newState = state with {
            IsRecording = isRecording,
            IsConnected = isConnected,
            IsVoiceActive = isVoiceActive,
        };

        if (state != newState)
            _state.Value = state with {
                IsRecording = isRecording,
                IsConnected = isConnected,
                IsVoiceActive = isVoiceActive,
            };
        _recordingActivity
            ?.AddSentrySimulatedEvent(new ActivityEvent("Recording state changed",
                tags: new ActivityTagsCollection {
                    { "AC." + nameof(AudioRecorderState.IsRecording), isRecording },
                    { "AC." + nameof(AudioRecorderState.IsConnected), isConnected },
                    { "AC." + nameof(AudioRecorderState.IsVoiceActive), isVoiceActive },
                }));
        DebugLog?.LogDebug("Chat #{ChatId}: recording state changed: {State}", state.ChatId, state);
    }

    // Private methods

    private async Task<bool> StopRecordingUnsafe()
    {
        var chatId = _state.Value.ChatId;
        if (chatId.IsNone || _jsRef == null!)
            return true; // Nothing to do

        // This method should reliably stop the recording, so we don't use normal cancellation here
        try {
            await _jsRef.InvokeVoidAsync("stopRecording", CancellationToken.None)
                .AsTask().WaitAsync(StopRecordingTimeout).ConfigureAwait(false);
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
        var state = await _jsRef.InvokeAsync<string>("checkPermission", CancellationToken.None)
            .AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
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
        return await _jsRef.InvokeAsync<bool>("requestPermission", CancellationToken.None)
            .AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    // MarkXxx

    private void MarkStarting(ChatId chatId)
    {
        var currentState = _state.Value;
        var (_, isRecording, isConnected, isVoiceActive) = currentState;
        _state.Value = new AudioRecorderState(chatId) {
            IsRecording = isRecording,
            IsConnected = isConnected,
            IsVoiceActive = isVoiceActive,
        };
        _recordingActivity = BlazorUITrace.StartActivity("AudioRecording");
        _recordingActivity
            ?.SetTag("AC." + nameof(ChatId), chatId)
            .AddSentrySimulatedEvent(new ActivityEvent("Recoding is starting",
                tags: new ActivityTagsCollection {
                    { "AC." + nameof(AudioRecorderState.IsRecording), isRecording },
                    { "AC." + nameof(AudioRecorderState.IsConnected), isConnected },
                    { "AC." + nameof(AudioRecorderState.IsVoiceActive), isVoiceActive },
                }));
        DebugLog?.LogDebug("Chat #{ChatId}: recording is starting, {State}", chatId, _state.Value);
    }

    private void MarkStopped()
    {
        var currentState = _state.Value;
        var (_, isRecording, isConnected, isVoiceActive) = currentState;
        _state.Value = AudioRecorderState.Idle with {
            IsRecording = isRecording,
            IsConnected = isConnected,
            IsVoiceActive = isVoiceActive,
        };
        _recordingActivity
            ?.AddSentrySimulatedEvent(new ActivityEvent("Recording is stopped",
                tags: new ActivityTagsCollection {
                    { "AC." + nameof(AudioRecorderState.IsRecording), isRecording },
                    { "AC." + nameof(AudioRecorderState.IsConnected), isConnected },
                    { "AC." + nameof(AudioRecorderState.IsVoiceActive), isVoiceActive },
                }));
        _recordingActivity?.Dispose();
        DebugLog?.LogDebug("Recording is stopped, {State}", _state.Value);
    }

    public class AudioDiagnosticsState
    {
        public bool? IsPlayerInitialized { get; init; }
        public bool? IsRecorderInitialized { get; init; }
        public bool? HasMicrophonePermission { get; init; }
        public bool? IsAudioContextSourceActive { get; init; }
        public bool? IsAudioContextActive { get; init; }
        public bool? HasMicrophoneStream { get; init; }
        public bool? IsVadActive { get; init; }
        public VadEvent? LastVadEvent { get; init; }
        public long? LastVadFrameProcessedAt { get; init; }
        public bool? IsConnected { get; init; }
        public long? LastFrameProcessedAt { get; init; }
        public string? VadWorkletState { get; init; }
        public long? LastVadWorkletFrameProcessedAt { get; init; }
        public string? EncoderWorkletState { get; init; }
        public long? LastEncoderWorkletFrameProcessedAt { get; init; }

        public override string ToString()
            => $"{nameof(AudioDiagnosticsState)} {{ {nameof(IsPlayerInitialized)}: {IsPlayerInitialized}, {nameof(IsRecorderInitialized)}: {IsRecorderInitialized}, {nameof(HasMicrophonePermission)}: {HasMicrophonePermission}, {nameof(IsAudioContextSourceActive)}: {IsAudioContextSourceActive}, {nameof(IsAudioContextActive)}: {IsAudioContextActive}, {nameof(HasMicrophoneStream)}: {HasMicrophoneStream}, {nameof(IsVadActive)}: {IsVadActive}, {nameof(LastVadEvent)}: {LastVadEvent}, {nameof(LastVadFrameProcessedAt)}: {LastVadFrameProcessedAt}, {nameof(IsConnected)}: {IsConnected}, {nameof(LastFrameProcessedAt)}: {LastFrameProcessedAt}, {nameof(VadWorkletState)}: {VadWorkletState}, {nameof(LastVadWorkletFrameProcessedAt)}: {LastVadWorkletFrameProcessedAt}, {nameof(EncoderWorkletState)}: {EncoderWorkletState}, {nameof(LastEncoderWorkletFrameProcessedAt)}: {LastEncoderWorkletFrameProcessedAt} }}";
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
