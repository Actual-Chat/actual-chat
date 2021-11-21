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
    private Task<Unit> _whenReadyToBufferMore;
    private IJSObjectReference? _jsRef;
    private ILogger? DebugLog => DebugMode ? Log : null;
    private bool DebugMode { get; } = Constants.DebugMode.AudioPlayback;

    public AudioSource AudioSource => (AudioSource)Source;

    public AudioTrackPlayer(
        PlayMediaTrackCommand command,
        BlazorCircuitContext circuitContext,
        IJSRuntime js,
        MomentClockSet clocks,
        ILogger<AudioTrackPlayer> log)
        : base(command, clocks, log)
    {
        _circuitContext = circuitContext;
        _js = js;
        _header = AudioSource.Format.ToBlobPart().Data;
        ReadyToBufferMore();
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
    public Task OnReadyToBufferMore(double? offset, int? readyState)
    {
        DebugLog?.LogWarning(
            "Ready to buffer more audio data. Offset = {Offset}, readyState = {ReadyState}",
            offset, readyState);
        ReadyToBufferMore();
        return Task.CompletedTask;
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
                var bufferedDuration = await _jsRef.InvokeAsync<double>("appendAudio", cancellationToken, chunk, offset);
                if (bufferedDuration > 10) // > 10 seconds
                    await _whenReadyToBufferMore.WithTimeout(TimeSpan.FromSeconds(5), cancellationToken);
            }).ConfigureAwait(false);

    private void ReadyToBufferMore()
    {
        if (_whenReadyToBufferMore != null!)
            TaskSource.For(_whenReadyToBufferMore).TrySetResult(default);
        _whenReadyToBufferMore = TaskSource.New<Unit>(true).Task;
    }

    private Task CircuitInvoke(Func<Task> workItem)
    {
        try {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            return _circuitContext.IsDisposing || _circuitContext.RootComponent == null
                ? Task.CompletedTask
                : _circuitContext.RootComponent.GetDispatcher().InvokeAsync(workItem);
        }
        catch (Exception e) {
            Log.LogError(e, $"{nameof(CircuitInvoke)} failed");
        }
        return Task.CompletedTask;
    }
}
