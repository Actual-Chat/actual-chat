using ActualChat.Media;
using ActualChat.MediaPlayback;
using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Components;

namespace ActualChat.UI.Blazor.App.Components.VideoPanel;

public sealed class VideoPlaybackEngineFactory(IServiceProvider services) : IVideoPlaybackEngineFactory
{
    private IJSObjectReference? _videoPanelJsRef;

    public void SetVideoPanelJsRef(IJSObjectReference videoPanelJsRef)
        => _videoPanelJsRef = videoPanelJsRef;

    public IVideoPlaybackEngine Create(
        string playerId,
        VideoStreamInfo streamInfo,
        IMediaSource source,
        IVideoPlayerBackend backend)
    {
        if (_videoPanelJsRef == null)
            throw new InvalidOperationException("VideoPanelJsRef must be set before creating a playback engine.");

        return new VideoPlaybackEngine(playerId, streamInfo, source, backend, _videoPanelJsRef, services);
    }
}
