using System.Reflection;
using ActualChat.Audio.UI.Blazor.Components;
using ActualChat.Audio.UI.Blazor.Module;
using ActualChat.Media;
using ActualChat.MediaPlayback;

namespace ActualChat.Audio.UI.Blazor;

public class AudioTrackPlayer : TrackPlayer, IAudioPlayerBackend
{
    private BlazorCircuitContext CircuitContext { get; }
    private IJSRuntime JS { get; }
    private byte[] Header { get; }
    private DotNetObjectReference<IAudioPlayerBackend>? BlazorRef { get; set; }
    private IJSObjectReference? JSRef { get; set; }
    private Task<Unit> WhenBufferReady { get; set; } = TaskSource.New<Unit>(true).Task;

    public AudioSource AudioSource => (AudioSource)Source;

    public AudioTrackPlayer(Playback playback, PlayTrackCommand command)
        : base(playback, command)
    {
        CircuitContext = Services.GetRequiredService<BlazorCircuitContext>();
        JS = Services.GetRequiredService<IJSRuntime>();
        Header = AudioSource.Format.Serialize();
        UpdateBufferReadyState(true);
    }

    [JSInvokable]
    public async Task OnPlaybackEnded(int? errorCode, string? errorMessage)
    {
        Exception? error = null;
        if (errorMessage != null) {
            error = new TargetInvocationException(
                $"Playback stopped with an error, code = {errorCode}, message = '{errorMessage}'.",
                null);
            Log.LogError(error, "Playback stopped with an error");
        }

        OnStopped(error);

        var jsRef = JSRef;
        JSRef = null;

        if (jsRef != null)
            await jsRef.DisposeAsync().ConfigureAwait(true);
    }

    [JSInvokable]
    public Task OnPlaybackTimeChanged(double? offset)
    {
        if (offset != null)
            OnPlayedTo(TimeSpan.FromSeconds(offset.Value));
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnChangeReadiness(bool isBufferReady, double? offset, int? readyState)
    {
        DebugLog?.LogDebug(
            "bufferReady: {BufferReadiness}, Offset = {Offset}, mediaReadyState = {MediaElementReadyState}",
            isBufferReady,
            offset,
            ToMediaElementReadyState(readyState));

        UpdateBufferReadyState(isBufferReady);
        return Task.CompletedTask;

        static string ToMediaElementReadyState(int? state) => state switch {
            0 => "HAVE_NOTHING",
            1 => "HAVE_METADATA",
            2 => "HAVE_CURRENT_DATA",
            3 => "HAVE_FUTURE_DATA",
            4 => "HAVE_ENOUGH_DATA",
            _ => $"UNKNOWN:{state?.ToString(CultureInfo.InvariantCulture) ?? "(null)"}",
        };
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
                    case StartPlaybackCommand:
                        BlazorRef = DotNetObjectReference.Create<IAudioPlayerBackend>(this);
                        JSRef = await JS.InvokeAsync<IJSObjectReference>(
                            $"{AudioBlazorUIModule.ImportName}.AudioPlayer.create",
                            BlazorRef, DebugMode
                            ).ConfigureAwait(true);
                        await JSRef!.InvokeVoidAsync("initialize", Header).ConfigureAwait(true);
                        break;
                    case StopPlaybackCommand stop:
                        if (JSRef == null)
                            break;

                        if (stop.Immediately)
                            await JSRef.InvokeVoidAsync("stop", null).ConfigureAwait(true);
                        else
                            await JSRef.InvokeVoidAsync("endOfStream").ConfigureAwait(true);
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
                    return;

                var chunk = frame.Data;
                var offset = frame.Offset.TotalSeconds;
                _ = JSRef.InvokeVoidAsync("appendAudioAsync", cancellationToken, chunk, offset);
                try {
                    await WhenBufferReady.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException) {
                    Log.LogError(
                        "ProcessMediaFrame: ready-to-buffer wait timed out, frame: (offset: {FrameOffset})",
                        frame.Offset);
                    throw;
                }
            }).ConfigureAwait(false);

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
