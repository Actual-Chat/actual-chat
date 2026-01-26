using ActualChat.Media;
using ActualChat.MediaPlayback;
using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.Video;

namespace ActualChat.UI.Blazor.App.Components;

public sealed class VideoPlaybackEngine(
    string id,
    VideoStreamInfo streamInfo,
    IMediaSource source,
    IVideoPlayerBackend playerBackend,
    IJSObjectReference videoPanelJsRef,
    IServiceProvider services)
    : IVideoPlaybackEngine
{
    private static readonly string JSStartRemoteStreamMethod = "startRemoteStream";
    private static readonly string JSPushRemoteFrameMethod = "pushRemoteFrame";
    private static readonly string JSStopRemoteStreamMethod = "stopRemoteStream";

    private readonly DotNetObjectReference<IVideoPlayerBackend> _blazorRef = DotNetObjectReference.Create(playerBackend);

    private bool _isPlaying;
    private Task? _whenStarted;

    private Dispatcher Dispatcher => field ??= services.GetRequiredService<Dispatcher>();
    private ILogger Log => field ??= services.LogFor<VideoPlaybackEngine>();

    public async Task Play(CancellationToken cancellationToken)
    {
        if (_isPlaying)
            return;

        var videoSource = (VideoSource)source;
        var format = videoSource.Format;

        var whenStarted = videoPanelJsRef.InvokeVoidAsync(
            JSStartRemoteStreamMethod,
            CancellationToken.None,
            _blazorRef,
            streamInfo.StreamId.Value,
            streamInfo.AuthorId.Value,
            format.Codec,
            format.Width,
            format.Height,
            format.CodecSettings);
        _whenStarted = whenStarted.AsTask();
        await whenStarted.ConfigureAwait(false);
        _isPlaying = true;
    }

    public async Task Stop(CancellationToken cancellationToken)
    {
        if (!_isPlaying && _whenStarted != null)
            await _whenStarted.ConfigureAwait(false);

        if (!_isPlaying)
            return;

        _isPlaying = false;
        await videoPanelJsRef.InvokeVoidAsync(
            JSStopRemoteStreamMethod,
            CancellationToken.None,
            streamInfo.StreamId.Value).ConfigureAwait(false);
    }

    public ValueTask PushFrame(MediaFrame frame, CancellationToken cancellationToken)
    {
        if (!_isPlaying)
            throw StandardError.StateTransition(GetType(), "Can't process media frame before initialization.");

        var videoFrame = (VideoFrame)frame;
        _ = videoPanelJsRef.InvokeVoidAsync(
            JSPushRemoteFrameMethod,
            cancellationToken,
            streamInfo.StreamId.Value,
            frame.Data,
            frame.Offset.TotalMilliseconds,
            frame.Duration.TotalMilliseconds,
            videoFrame.IsKeyFrame,
            videoFrame.Description); // Codec description (SPS/PPS for H.264) - only present on keyframes

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        var blazorRef = _blazorRef;
        await InvokeAsync(async () => {
            if (_isPlaying) {
                _isPlaying = false;
                await videoPanelJsRef.InvokeVoidAsync(
                    JSStopRemoteStreamMethod,
                    CancellationToken.None,
                    streamInfo.StreamId.Value).ConfigureAwait(false);
            }
            blazorRef.DisposeSilently();
        });
    }

    private Task InvokeAsync(Func<Task> workItem)
        => InvokeAsync(async () => { await workItem().ConfigureAwait(false); return true; });

    private Task<TResult?> InvokeAsync<TResult>(Func<Task<TResult?>> workItem)
    {
        try {
            return Dispatcher.InvokeAsync(workItem);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, $"[VideoPlaybackEngine #{{VideoPlayerId}}] {nameof(InvokeAsync)} failed", id);
            throw;
        }
    }
}
