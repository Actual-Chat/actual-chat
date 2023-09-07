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
    private IJSObjectReference _jsRef = null!;

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
            _jsRef = await JS.InvokeAsync<IJSObjectReference>(JSCreateMethod, _blazorRef)
                .ConfigureAwait(false);
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

        MarkStarting(chatId);
        var cts = cancellationToken.CreateLinkedTokenSource();
        cts.CancelAfter(StartRecordingTimeout);
        try {
            var isStarted = await _jsRef
                .InvokeAsync<bool>("startRecording", cts.Token, chatId, repliedChatEntryId)
                .ConfigureAwait(false);
            if (!isStarted) {
                MicrophonePermission.Reset();
                Log.LogWarning(nameof(StartRecording) + ": chat #{ChatId} - can't access the microphone", chatId);
                // Cancel recording
                MarkStopped();
                throw new AudioRecorderException(
                    "Can't access the microphone - please check if microphone access permission is granted.");
            }
        }
        catch (Exception e) when (e is not AudioRecorderException) {
            if (e is not OperationCanceledException)
                // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
                DebugLog?.LogDebug(nameof(StartRecording) + " is cancelled");
            else
                // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
                Log.LogError(e, nameof(StartRecording) + " failed");

            await StopRecordingUnsafe().ConfigureAwait(false);

            if (e is OperationCanceledException) {
                if (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                    throw new AudioRecorderException("Failed to start recording in time.", e);
                throw;
            }
            throw new AudioRecorderException("Failed to start recording.", e);
        }
        finally {
            cts.CancelAndDisposeSilently();
        }
    }

    public async Task<bool> StopRecording(CancellationToken cancellationToken = default)
    {
        await WhenInitialized.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var _ = await _stateLock.Lock(cancellationToken).ConfigureAwait(false);
        return await StopRecordingUnsafe().ConfigureAwait(false);
    }

    public ValueTask Reconnect(CancellationToken cancellationToken)
        => _jsRef.InvokeVoidAsync("reconnect", cancellationToken);

    public ValueTask ConversationSignal(CancellationToken cancellationToken)
        => _jsRef.InvokeVoidAsync("conversationSignal", cancellationToken);

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

        _state.Value = state with {
            IsRecording = isRecording,
            IsConnected = isConnected,
            IsVoiceActive = isVoiceActive,
        };
        DebugLog?.LogDebug("Chat #{ChatId}: recording state changed: {State}", state.ChatId, state);
    }

    // Private methods

    private async Task<bool> StopRecordingUnsafe()
    {
        var chatId = _state.Value.ChatId;
        if (chatId.IsNone || _jsRef == null!)
            return true; // Nothing to do

        // This method should reliably stop the recording, so we don't use normal cancellation here
        var cts = new CancellationTokenSource(StopRecordingTimeout);
        try {
            await _jsRef.InvokeVoidAsync("stopRecording", cts.Token).ConfigureAwait(false);
        }
        catch (JSDisconnectedException) { } // Circuit is disposed or disposing
        catch (ObjectDisposedException) { } // Circuit is disposed or disposing
        catch (Exception e) {
            var reason = cts.IsCancellationRequested ? "timed out" : "failed";
            // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
            Log.LogError(e, $"{nameof(StopRecordingUnsafe)}: chat #{{ChatId}} - {reason}, recorder state is in doubt", chatId);
            return false;
        }
        finally {
            cts.CancelAndDisposeSilently();
        }
        MarkStopped();
        return true;
    }

    internal async Task<bool?> CheckPermission(CancellationToken cancellationToken = default)
    {
        await WhenInitialized.ConfigureAwait(false);
        var state = await _jsRef.InvokeAsync<string>("checkPermission", cancellationToken).ConfigureAwait(false);
        return state switch {
            "prompt" => null,
            "denied" => false,
            "granted" => true,
            _ => null,
        };
    }

    internal async Task<bool> RequestPermission(CancellationToken cancellationToken = default)
    {
        await WhenInitialized.ConfigureAwait(false);
        return await _jsRef.InvokeAsync<bool>("requestPermission", cancellationToken).ConfigureAwait(false);
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
        DebugLog?.LogDebug("Chat #{ChatId}: recording is starting", chatId);
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
        DebugLog?.LogDebug("Recording is stopped");
    }
}
