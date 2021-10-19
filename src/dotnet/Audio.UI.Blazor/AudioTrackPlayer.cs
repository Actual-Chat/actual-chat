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

    public AudioSource AudioSource => (AudioSource)Track.Source;

    public AudioTrackPlayer(
        MediaTrack mediaTrack,
        BlazorCircuitContext circuitContext,
        IJSRuntime js,
        ILogger<AudioTrackPlayer> log)
        : base(mediaTrack, log)
    {
        _circuitContext = circuitContext;
        _js = js;
    }

    protected override async ValueTask OnPlayStart(TimeSpan offset)
    {
        if (_jsRef == null)
            await CircuitInvoke(async () => {
                    _blazorRef = DotNetObjectReference.Create<IAudioPlayerBackend>(this);
                    _jsRef = await _js.InvokeAsync<IJSObjectReference>(
                        $"{AudioBlazorUIModule.ImportName}.AudioPlayer.create",
                        _blazorRef);
                })
                .ConfigureAwait(false);

        var header = Convert.FromBase64String(AudioSource.Format.CodecSettings);
        await CircuitInvoke(async () => {
                await _jsRef!.InvokeVoidAsync("initialize", header, offset.TotalSeconds);
            })
            .ConfigureAwait(false);
    }

    protected override async ValueTask OnPlayNextFrame(PlayingMediaFrame nextFrame)
    {
        var chunk = nextFrame.Frame.Data;
        var offsetSecs = nextFrame.Frame.Offset.TotalSeconds;
        if (_jsRef != null)
            await CircuitInvoke(async () => {
                    await _jsRef.InvokeVoidAsync("appendAudio", chunk, offsetSecs);
                })
                .ConfigureAwait(false);
    }

    protected override async ValueTask OnPlayStop(bool stopImmediately, CancellationToken cancellationToken)
    {
        if (_jsRef == null)
            return;

        await CircuitInvoke(async () => {
                if (stopImmediately)
                    await _jsRef.InvokeVoidAsync("stop", CancellationToken.None, "network");
                else
                    await _jsRef.InvokeVoidAsync("endOfStream", cancellationToken);
                await _jsRef.DisposeAsync();
            })
            .ConfigureAwait(false);
        _jsRef = null;
    }

    protected Task CircuitInvoke(Func<Task> workItem)
        => _circuitContext.RootComponent.GetDispatcher().InvokeAsync(workItem);
}
