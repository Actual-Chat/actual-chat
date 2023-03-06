using ActualChat.Audio.UI.Blazor.Module;
using Stl.Locking;

namespace ActualChat.Audio.UI.Blazor.Components;

public class AudioRecorder : IAsyncDisposable
{
    private static readonly TimeSpan StartRecordingTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan StopRecordingTimeout = TimeSpan.FromSeconds(3);

    private readonly AsyncLock _stateLock = new (ReentryMode.UncheckedDeadlock);
    private readonly IMutableState<AudioRecorderState> _state;
    private IJSObjectReference _jsRef = null!;

    private ILogger Log { get; }
    private ILogger? DebugLog => DebugMode ? Log : null;
    private bool DebugMode => Constants.DebugMode.AudioRecording;

    private Session Session { get; }
    private IJSRuntime JS { get; }

    public Task WhenInitialized { get; }
    public IState<AudioRecorderState> State => _state;

    public AudioRecorder(IServiceProvider services)
    {
        Log = services.LogFor<AudioRecorder>();
        Session = services.GetRequiredService<Session>();
        JS = services.GetRequiredService<IJSRuntime>();
        _state = services.StateFactory().NewMutable(
            AudioRecorderState.Idle,
            StateCategories.Get(GetType(), nameof(State)));
        WhenInitialized = Initialize();

        async Task Initialize()
        {
            _jsRef = await JS.InvokeAsync<IJSObjectReference>(
                    $"{AudioBlazorUIModule.ImportName}.AudioRecorder.create",
                    Session.Id)
                .ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        using var _ = await _stateLock.Lock().ConfigureAwait(false);
        await _jsRef.DisposeSilentlyAsync("dispose").ConfigureAwait(false);
        _jsRef = null!;
    }

    public async Task<bool> RequestPermission(CancellationToken cancellationToken = default)
    {
        await WhenInitialized.ConfigureAwait(false);
        return await _jsRef.InvokeAsync<bool>("requestPermission", cancellationToken).ConfigureAwait(false);
    }

    public async Task StartRecording(ChatId chatId, CancellationToken cancellationToken = default)
    {
        if (chatId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(chatId));

        await WhenInitialized.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var _ = await _stateLock.Lock(cancellationToken).ConfigureAwait(false);
        var state = _state.Value;
        if (state.ChatId == chatId) {
            if (state.IsRecording)
                return; // Already started
        }
        else if (!state.ChatId.IsNone)
            await StopRecordingUnsafe();

        MarkStarting(chatId);
        using var cts = CancellationSource.NewLinked(cancellationToken, StartRecordingTimeout);
        try {
            var isStarted = await _jsRef
                .InvokeAsync<bool>("startRecording", cts.Token, chatId)
                .ConfigureAwait(false);
            if (!isStarted) {
                Log.LogWarning(nameof(StartRecording) + ": chat #{ChatId} - can't access the microphone", chatId);
                // Cancel recording
                MarkStopped();
                throw new AudioRecorderException(
                    "Can't access the microphone - please check if microphone access permission is granted.");
            }
            MarkStarted();
        }
        catch (Exception e) when (e is not AudioRecorderException) {
            if (e is not OperationCanceledException)
                // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
                DebugLog?.LogDebug(nameof(StartRecording) + " is cancelled");
            else
                // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
                Log.LogError(e, nameof(StartRecording) + " failed");

            await StopRecordingUnsafe().ConfigureAwait(false);

            if (e is OperationCanceledException)
                throw;
            throw new AudioRecorderException("Failed to start recording.", e);
        }
    }

    public async Task<bool> StopRecording(CancellationToken cancellationToken = default)
    {
        await WhenInitialized.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var _ = await _stateLock.Lock(cancellationToken).ConfigureAwait(false);
        return await StopRecordingUnsafe().ConfigureAwait(false);
    }

    // Private methods

    private async Task<bool> StopRecordingUnsafe()
    {
        var chatId = _state.Value.ChatId;
        if (chatId.IsNone || _jsRef == null!)
            return true; // Nothing to do

        // This method should reliably stop the recording, so we don't use normal cancellation here
        using var cts = CancellationSource.New(StopRecordingTimeout);
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
        MarkStopped();
        return true;
    }

    // MarkXxx

    private void MarkStarting(ChatId chatId)
    {
        _state.Value = new AudioRecorderState(chatId);
        DebugLog?.LogDebug("Chat #{ChatId}: recording is starting", chatId);
    }

    private void MarkStarted()
    {
        var state = _state.Value;
        if (state.ChatId.IsNone)
            throw StandardError.Internal("Something is off: MarkStarted() is called, but ChatId.IsNone == true.");

        _state.Value = state with { IsRecording = true };
        DebugLog?.LogDebug("Chat #{ChatId}: recording is started", state.ChatId);
    }

    private void MarkStopped()
    {
        _state.Value = AudioRecorderState.Idle;
        DebugLog?.LogDebug("Recording is stopped");
    }
}
