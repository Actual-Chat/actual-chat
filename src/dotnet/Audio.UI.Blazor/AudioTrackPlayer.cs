using System.Reflection;
using ActualChat.Audio.UI.Blazor.Components;
using ActualChat.Media;
using ActualChat.Playback;
using Microsoft.JSInterop;
using Stl.Fusion.Blazor;

namespace ActualChat.Audio.UI.Blazor;

public class AudioTrackPlayer : MediaTrackPlayer, IAudioPlayerBackend
{
    private readonly BlazorCircuitContext _circuitContext;
    private readonly byte[] _header;
    private readonly IJSRuntime _js;
    private DotNetObjectReference<IAudioPlayerBackend>? _blazorRef;
    private IJSObjectReference? _jsRef;
    private ILogger? DebugLog => DebugMode ? Log : null;
    private bool DebugMode { get; } = Constants.DebugMode.AudioPlayback;
    private Task<Unit> _jsReadyToBuffer = TaskSource.New<Unit>(true).Task;

    public AudioSource AudioSource => (AudioSource)Source;

    public AudioTrackPlayer(
        MediaPlaybackState? parentState,
        PlayMediaTrackCommand command,
        BlazorCircuitContext circuitContext,
        IJSRuntime js,
        MomentClockSet clocks,
        ILogger<AudioTrackPlayer> log)
        : base(parentState, command, clocks, log)
    {
        _circuitContext = circuitContext;
        _js = js;
        _header = AudioSource.Format.ToBlobPart().Data;
        SetJsReadyToBuffer(true);
    }

    private void SetJsReadyToBuffer(bool readiness)
    {
        if (readiness) {
            if (_jsReadyToBuffer.IsCompleted)
                return;
            TaskSource.For(_jsReadyToBuffer).TrySetResult(default);
        }
        else {
            if (!_jsReadyToBuffer.IsCompleted)
                return;
            _jsReadyToBuffer = TaskSource.New<Unit>(true).Task;
        }
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

        var jsRef = _jsRef;
        _jsRef = null;

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

        SetJsReadyToBuffer(isBufferReady);
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

    protected override async ValueTask ProcessCommand(MediaTrackPlayerCommand command)
        => await CircuitInvoke(
            async () => {
                switch (command) {
                    case StartPlaybackCommand:
                        _blazorRef = DotNetObjectReference.Create<IAudioPlayerBackend>(this);
                        _jsRef = await _js.InvokeAsync<IJSObjectReference>(
                            $"{AudioBlazorUIModule.ImportName}.AudioPlayer.create",
                            _blazorRef,
                            DebugMode);
                        await _jsRef!.InvokeVoidAsync("initialize", _header);
                        break;
                    case StopPlaybackCommand stop:
                        if (_jsRef == null)
                            break;

                        if (stop.Immediately)
                            await _jsRef.InvokeVoidAsync("stop", null);
                        else
                            await _jsRef.InvokeVoidAsync("endOfStream");
                        break;
                    case SetTrackVolumeCommand setVolume:
                        // TODO: Implement this
                        break;
                    default:
                        throw new NotSupportedException($"Unsupported command type: '{command.GetType()}'.");
                }
            }).ConfigureAwait(false);

    protected override async ValueTask ProcessMediaFrame(MediaFrame frame, CancellationToken cancellationToken)
        => await CircuitInvoke(
            async () => {
                if (_jsRef == null)
                    return;

                var chunk = frame.Data;
                var offset = frame.Offset.TotalSeconds;
                _ = _jsRef.InvokeVoidAsync("appendAudioAsync", cancellationToken, chunk, offset);
                try {
                    await _jsReadyToBuffer.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
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
            return _circuitContext.IsDisposing || _circuitContext.RootComponent == null
                ? Task.FromResult(default(TResult?))
                : _circuitContext.RootComponent.GetDispatcher().InvokeAsync(workItem);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, $"{nameof(CircuitInvoke)} failed");
            throw;
        }
    }
}
