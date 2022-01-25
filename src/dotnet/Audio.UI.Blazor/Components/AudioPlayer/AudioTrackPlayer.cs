using System.Reflection;
using ActualChat.Audio.UI.Blazor.Module;
using ActualChat.Media;
using ActualChat.MediaPlayback;

namespace ActualChat.Audio.UI.Blazor.Components;

public class AudioTrackPlayer : TrackPlayer, IAudioPlayerBackend
{
    private BlazorCircuitContext CircuitContext { get; }
    private IJSRuntime JS { get; }
    private DotNetObjectReference<IAudioPlayerBackend>? BlazorRef { get; set; }
    private IJSObjectReference? JSRef { get; set; }
    private Task<Unit> WhenBufferReady { get; set; } = TaskSource.New<Unit>(true).Task;
    private bool _isStopSent = false;

    public AudioSource AudioSource => (AudioSource)Source;

    public AudioTrackPlayer(Playback playback, PlayTrackCommand command)
        : base(playback, command)
    {
        CircuitContext = Services.GetRequiredService<BlazorCircuitContext>();
        JS = Services.GetRequiredService<IJSRuntime>();
        UpdateBufferReadyState(true);
    }

    [JSInvokable]
    public Task OnPlaybackEnded(string? errorMessage)
    {
        Exception? error = null;
        if (errorMessage != null) {
            error = new TargetInvocationException(
                $"Playback stopped with an error, message = '{errorMessage}'.",
                null);
            Log.LogError(error, "Playback stopped with an error");
        }
        OnStopped(error);
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnPlaybackTimeChanged(double offset)
    {
        OnPlayedTo(TimeSpan.FromSeconds(offset));
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnChangeReadiness(bool isBufferReady)
    {
        DebugLog?.LogDebug("[{TrackPlayerId}] OnChangeReadiness(bufferReady:{BufferReadiness})", isBufferReady);
        UpdateBufferReadyState(isBufferReady);
        return Task.CompletedTask;
    }

    [JSInvokable]
    public void OnVolumeChanged(double? volume)
    {
        if (volume != null)
            OnVolumeSet(volume.Value);
    }

    protected override async ValueTask ProcessCommand(TrackPlayerCommand command)
        => await CircuitInvoke(
            async () => {
                switch (command) {
                    case InitializeCommand:
                        BlazorRef = DotNetObjectReference.Create<IAudioPlayerBackend>(this);
                        JSRef = await JS.InvokeAsync<IJSObjectReference>(
                                $"{AudioBlazorUIModule.ImportName}.AudioPlayer.create",
                                CancellationToken.None,
                                BlazorRef,
                                DebugMode
                            ).ConfigureAwait(true);
                        break;
                    case StartPlaybackCommand:
                        if (JSRef == null)
                            throw new InvalidOperationException($"{nameof(StartPlaybackCommand)}: Initialize command should be called first.");
                        _ = JSRef.InvokeVoidAsync("init", CancellationToken.None, AudioSource.Format.Serialize());
                        break;
                    case StopPlaybackCommand:
                        if (!_isStopSent) {
                            if (JSRef == null)
                                throw new InvalidOperationException($"{nameof(StopPlaybackCommand)}: Initialize command should be called first.");
                            _ = JSRef.InvokeVoidAsync("stop", CancellationToken.None);
                            _isStopSent = true;
                        }
                        break;
                    case EndCommand:
                        if (JSRef == null)
                            throw new InvalidOperationException($"{nameof(EndCommand)}: Initialize command should be called first.");
                        _ = JSRef.InvokeVoidAsync("end", CancellationToken.None);
                        break;
                    case SetTrackVolumeCommand:
                        // TODO: Implement this
                        break;
                    default:
                        throw new NotSupportedException($"Unsupported command type: '{command.GetType()}'.");
                }
            }).ConfigureAwait(false);

    protected override async ValueTask ProcessMediaFrame(MediaFrame frame, CancellationToken cancellationToken)
        => await CircuitInvoke(
            async () => {
                if (JSRef == null)
                    throw new InvalidOperationException("Can't process media frame before initialization.");

                var chunk = frame.Data;
                _ = JSRef.InvokeVoidAsync("data", cancellationToken, chunk);
                try {
                    await WhenBufferReady.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException) {
                    Log.LogError(
                        "ProcessMediaFrame: ready-to-buffer wait timed out, offset={FrameOffset}",
                        frame.Offset);
                }
            }).ConfigureAwait(false);

    protected override void OnStopped(Exception? error = null)
    {
        base.OnStopped(error);
        _ = CircuitInvoke(async () => {
            var (jsRef, blazorRef) = (JSRef, BlazorRef);
            (JSRef, BlazorRef) = (null, null);
            try {
                try {
                    if (jsRef != null)
                        await jsRef.DisposeAsync().ConfigureAwait(true);
                }
                finally {
                    blazorRef?.Dispose();
                }
            }
            catch (Exception e) {
                Log.LogError(e, "OnStopped failed while disposing the references");
            }
        });
    }

    private Task CircuitInvoke(Func<Task> workItem)
        => CircuitInvoke(async () => { await workItem().ConfigureAwait(false); return true; });
#pragma warning disable RCS1229
    private Task<TResult?> CircuitInvoke<TResult>(Func<Task<TResult?>> workItem)
    {
        try {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            return CircuitContext.IsDisposing || CircuitContext.RootComponent == null
                ? Task.FromResult(default(TResult?))
                : CircuitContext.RootComponent.GetDispatcher().InvokeAsync(workItem);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, $"{nameof(CircuitInvoke)} failed");
            throw;
        }
    }

    private void UpdateBufferReadyState(bool isBufferReady)
    {
        if (isBufferReady) {
            if (WhenBufferReady.IsCompleted)
                return;
            TaskSource.For(WhenBufferReady).TrySetResult(default);
        }
        else {
            if (!WhenBufferReady.IsCompleted)
                return;
            WhenBufferReady = TaskSource.New<Unit>(true).Task;
        }
    }
}
