using ActualChat.Audio.UI.Blazor.Components;
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
    private double _playedUpTo;

    public AudioSource AudioSource => (AudioSource)Command.Source;
    public byte[] Header { get; }


    public AudioTrackPlayer(
        PlayMediaTrackCommand command,
        BlazorCircuitContext circuitContext,
        IJSRuntime js,
        ILogger<AudioTrackPlayer> log)
        : base(command, log)
    {
        _circuitContext = circuitContext;
        _js = js;
        _delayTokenSource = new CancellationTokenSource();

        Header = Convert.FromBase64String(AudioSource.Format.CodecSettings);
    }

    [JSInvokable]
    public void OnPlaybackEnded(int? errorCode, string? errorMessage)
    {
        if (errorMessage != null)
            Log.LogError("Playback stopped with error. ErrorCode = {ErrorCode}, ErrorMessage = {ErrorMessage}",
                errorCode,
                errorMessage);

        _ = OnStopped();
    }

    [JSInvokable]
    public void OnDataWaiting(double offset, int readyState)
    {
        _delayTokenSource.Cancel();
        _delayTokenSource.Dispose();
        _delayTokenSource = new CancellationTokenSource();

        Log.LogWarning("Waiting for audio data. Offset = {Offset}, readyState = {readyState}", offset, readyState);
    }

    [JSInvokable]
    public void OnPlaybackTimeChanged(double offset)
    {
        _playedUpTo = offset;
        _ = OnPlayedTo(TimeSpan.FromSeconds(offset));
    }

    protected override async ValueTask EnqueueCommandInternal(MediaTrackPlayerCommand command)
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
                            await _jsRef.InvokeVoidAsync("stop", "network");
                        else
                            await _jsRef.InvokeVoidAsync("endOfStream");
                        await _jsRef.DisposeAsync();
                        break;
                    case PlayMediaFrameCommand playFrame:
                        var chunk = playFrame.Frame.Data;
                        var offset = playFrame.Frame.Offset.TotalSeconds;

                        if (offset > _playedUpTo + 10)
                            await Task.Delay(TimeSpan.FromSeconds(5), _delayTokenSource.Token);

                        await _jsRef!.InvokeVoidAsync("appendAudio", chunk, offset);
                        break;
                    case SetTrackVolumeCommand setVolume:
                        // TODO: Implement this
                        break;
                    default:
                        throw new NotSupportedException($"Unsupported command type: '{command.GetType()}'.");
                }
            })
            .ConfigureAwait(false);

    protected Task CircuitInvoke(Func<Task> workItem)
        => _circuitContext.IsDisposing
            ? Task.CompletedTask
            : _circuitContext.RootComponent.GetDispatcher().InvokeAsync(workItem);
}
