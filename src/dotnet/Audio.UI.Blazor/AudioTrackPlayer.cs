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
    private readonly IJSRuntime _js;
    private DotNetObjectReference<IAudioPlayerBackend>? _blazorRef;
    private CancellationTokenSource _delayTokenSource;
    private IJSObjectReference? _jsRef;
    private bool DebugMode { get; } = false;

    public AudioSource AudioSource => (AudioSource)Source;
    public byte[] Header { get; }

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
        _delayTokenSource = new CancellationTokenSource();

        Header = Convert.FromBase64String(AudioSource.Format.CodecSettings);
    }

    [JSInvokable]
    public void OnPlaybackEnded(int? errorCode, string? errorMessage)
    {
        Exception? error = null;
        if (errorMessage != null) {
            error = new TargetInvocationException(
                $"Playback stopped with an error, code = {errorCode}, message = '{errorMessage}'.",
                null);
            Log.LogError(error, "Playback stopped with an error");
        }

        OnStopped(error);
    }

    [JSInvokable]
    public void OnPlaybackTimeChanged(double? offset)
    {
        if (offset != null)
            OnPlayedTo(TimeSpan.FromSeconds(offset.Value));
    }

    [JSInvokable]
    public void OnDataWaiting(double? offset, int? readyState)
    {
        _delayTokenSource.Cancel();
        _delayTokenSource.Dispose();
        _delayTokenSource = new CancellationTokenSource();

        Log.LogWarning("Waiting for audio data. Offset = {Offset}, readyState = {readyState}", offset, readyState);
    }

    [JSInvokable]
    public void OnVolumeChanged(double? volume)
    {
        if (volume != null)
            OnVolumeSet(volume.Value);
    }

    protected override async ValueTask ProcessCommand(MediaTrackPlayerCommand command)
        => await CircuitInvoke(async () => {
                switch (command) {
                    case StartPlaybackCommand:
                        _blazorRef = DotNetObjectReference.Create<IAudioPlayerBackend>(this);
                        _jsRef = await _js.InvokeAsync<IJSObjectReference>(
                            $"{AudioBlazorUIModule.ImportName}.AudioPlayer.create",
                            _blazorRef);
                        await _jsRef!.InvokeVoidAsync("initialize", Header);
                        break;
                    case StopPlaybackCommand stop:
                        if (_jsRef == null)
                            break;

                        if (stop.Immediately)
                            await _jsRef.InvokeVoidAsync("stop", null);
                        else
                            await _jsRef.InvokeVoidAsync("endOfStream");
                        await _jsRef.DisposeAsync();
                        break;
                    case SetTrackVolumeCommand setVolume:
                        // TODO: Implement this
                        break;
                    default:
                        throw new NotSupportedException($"Unsupported command type: '{command.GetType()}'.");
                }
            })
            .ConfigureAwait(false);

    protected override async ValueTask ProcessMediaFrame(MediaFrame frame, CancellationToken cancellationToken)
        => await CircuitInvoke(async () => {
                using var cts = cancellationToken.LinkWith(_delayTokenSource.Token);
                var token = cts.Token;
                var chunk = frame.Data;
                var offset = frame.Offset.TotalSeconds;
                var buffered = await _jsRef!.InvokeAsync<double>("appendAudio", token, chunk, offset);

                if (buffered > 10)
                    await Task.Delay(TimeSpan.FromSeconds(5), token);
            })
            .ConfigureAwait(false);

    private Task CircuitInvoke(Func<Task> workItem)
        => _circuitContext.IsDisposing
            ? Task.CompletedTask
            : _circuitContext.RootComponent.GetDispatcher().InvokeAsync(workItem);
}
