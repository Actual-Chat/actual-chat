using ActualChat.Audio.UI.Blazor.Internal;
using ActualChat.Playback;
using Microsoft.JSInterop;

namespace ActualChat.Audio.UI.Blazor;

public class AudioTrackPlayer : MediaTrackPlayer, IAudioPlayerBackend
{
    private readonly IJSRuntime _js;

    private IJSObjectReference? _jsRef;
    private DotNetObjectReference<IAudioPlayerBackend>? _blazorRef;

    public AudioTrackPlayer(IJSRuntime js, MediaTrack mediaTrack) : base(mediaTrack)
    {
        _js = js;
    }

    public AudioSource AudioSource => (AudioSource)Track.Source;

    protected override async ValueTask OnPlayStart()
    {
        if (_jsRef == null) {
            _blazorRef = DotNetObjectReference.Create<IAudioPlayerBackend>(this);
            _jsRef = await _js.InvokeAsync<IJSObjectReference>($"{AudioBlazorUIModule.ImportName}.AudioPlayer.create",
                _blazorRef);
        }

        var header = Convert.FromBase64String(AudioSource.Format.CodecSettings);
        await _jsRef.InvokeVoidAsync("initialize", header);
    }

    protected override async ValueTask OnPlayNextFrame(PlayingMediaFrame nextFrame)
    {
        var chunk = nextFrame.Frame.Data;
        var offsetSecs = nextFrame.Frame.Offset.TotalSeconds;
        if (_jsRef != null)
            await _jsRef.InvokeVoidAsync("appendAudio", chunk, offsetSecs);
    }

    protected override ValueTask OnPlayStop()
    {
        return _jsRef is { } jsRef
            ? jsRef.InvokeVoidAsync("stop", args: null)
            : ValueTask.CompletedTask;
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        try {
            await OnPlayStop();
            if (_jsRef != null)
                await _jsRef.DisposeAsync().ConfigureAwait(false);
        }
        catch {
            // ignored
        }

        _jsRef = null;
    }
}
