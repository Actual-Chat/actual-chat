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
    private IJSObjectReference? _jsRef;
    private bool DebugMode { get; } = false;

    public AudioSource AudioSource => (AudioSource) Command.Source;
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
        Header = Convert.FromBase64String(AudioSource.Format.CodecSettings);
    }

    [JSInvokable]
    public void OnPlaybackTimeChanged(double offset)
        => OnPlayedTo(TimeSpan.FromSeconds(offset));

    [JSInvokable]
    public void OnPlaybackEnded(int? errorCode, string? errorMessage)
        => OnStopped();

    protected override async ValueTask EnqueueCommandInternal(MediaTrackPlayerCommand command)
    {
        await CircuitInvoke(async () => {
            switch (command) {
            case StartPlaybackCommand:
                _blazorRef = DotNetObjectReference.Create<IAudioPlayerBackend>(this);
                _jsRef = await _js.InvokeAsync<IJSObjectReference>(
                    $"{AudioBlazorUIModule.ImportName}.AudioPlayer.create",
                    _blazorRef, DebugMode);
                await _jsRef!.InvokeVoidAsync("initialize", Header, Command.StartOffset.TotalSeconds);
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
                await _jsRef!.InvokeVoidAsync("appendAudio", chunk, offset);
                break;
            case SetTrackVolumeCommand setVolume:
                // TODO: Implement this
                break;
            default:
                throw new NotSupportedException($"Unsupported command type: '{command.GetType()}'.");
            }
        }).ConfigureAwait(false);
    }

    protected Task CircuitInvoke(Func<Task> workItem)
        => _circuitContext.RootComponent.GetDispatcher().InvokeAsync(workItem);
}
