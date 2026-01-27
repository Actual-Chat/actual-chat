using ActualChat.Media;
using ActualChat.MediaPlayback;
using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Module;

namespace ActualChat.UI.Blazor.App.Components.VideoPanel;

public sealed class VideoPlaybackEngineFactory(IServiceProvider services) : IVideoPlaybackEngineFactory
{
    private static readonly string JSGetActiveInstanceMethod = $"{BlazorUIAppModule.ImportName}.VideoPanel.getActiveInstance";

    private IJSObjectReference? _videoPanelJsRef;

    public bool IsInitialized => _videoPanelJsRef != null;

    public async ValueTask EnsureInitialized(CancellationToken cancellationToken = default)
    {
        if (_videoPanelJsRef != null)
            return;

        var js = services.GetRequiredService<IJSRuntime>();
        _videoPanelJsRef = await js.InvokeAsync<IJSObjectReference?>(
            JSGetActiveInstanceMethod, cancellationToken).ConfigureAwait(false);
    }

    public IVideoPlaybackEngine Create(
        string playerId,
        VideoStreamInfo streamInfo,
        IMediaSource source,
        IVideoPlayerBackend backend)
    {
        var jsRef = _videoPanelJsRef
            ?? throw new InvalidOperationException("VideoPanel is not initialized. Call EnsureInitialized first.");

        return new VideoPlaybackEngine(playerId, streamInfo, source, backend, jsRef, services);
    }
}
