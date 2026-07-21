using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.App.Services.Video;
using ActualChat.UI.Blazor.Services;
using ActualChat.Video;
using CoreVideo;

namespace ActualChat.App.Maui.Video;

/// <summary>
/// Native <c>ILiveVideoStreams.GetStream</c> → VideoToolbox decode → JPEG → canvas
/// pipeline for one remote video tile on Mac Catalyst (where WebCodecs has no
/// <c>VideoDecoder</c>). Drop-in replacement for the JS player's decode/render path;
/// <see cref="VideoTrackPlayer"/> delegates to it. H.264 only for now.
/// </summary>
public sealed class MauiVideoPlayer : INativeVideoPlayer
{
    private const int TargetWidth = 640;
    private const float JpegQuality = 0.7f;

    private readonly AppUIHub _hub;
    private readonly VideoStreamInfo _streamInfo;
    private readonly Lock _sync = new();
    private readonly JpegFrameEmitter _emitter;
    private readonly AppleVideoDecoder _decoder;

    private Func<byte[], ValueTask>? _onFrame;
    private CancellationTokenSource? _cts;
    private Task? _pullTask;

    private ILiveVideoStreams LiveVideoStreams => field ??= _hub.Services.GetRequiredService<ILiveVideoStreams>();
    private ConnectivityUI ConnectivityUI => field ??= _hub.Services.GetRequiredService<ConnectivityUI>();
    private ILogger Log => field ??= _hub.Services.LogFor(GetType());

    public MauiVideoPlayer(AppUIHub hub, VideoStreamInfo streamInfo)
    {
        _hub = hub;
        _streamInfo = streamInfo;
        _emitter = new JpegFrameEmitter(TargetWidth, JpegQuality, hub.Services.LogFor(GetType()));
        _decoder = new AppleVideoDecoder(hub.Services.LogFor<AppleVideoDecoder>());
        _decoder.Decoded += OnDecoded;
    }

    public void Start(Func<byte[], ValueTask> onFrame)
    {
        lock (_sync) {
            if (_pullTask is not null)
                return;

            _onFrame = onFrame;
            _cts = new CancellationTokenSource();
            _pullTask = BackgroundTask.Run(() => PullLoop(_cts.Token), Log, "MauiVideoPlayer.PullLoop failed");
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        lock (_sync) {
            _onFrame = null;
            cts = _cts;
            _cts = null;
        }
        cts?.CancelAndDisposeSilently();
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        var pullTask = _pullTask;
        if (pullTask is not null)
            await pullTask.SilentAwait(false);
        _decoder.Decoded -= OnDecoded;
        _decoder.Dispose();
        _emitter.Dispose();
    }

    // Private methods

    private async Task PullLoop(CancellationToken cancellationToken)
    {
        var session = _hub.Session;
        var streamId = _streamInfo.StreamId;
        while (!cancellationToken.IsCancellationRequested) {
            try {
                await ConnectivityUI.WhenConnected(cancellationToken).ConfigureAwait(false);
                var rpcStream = await LiveVideoStreams.GetStream(session, streamId, cancellationToken)
                    .ConfigureAwait(false);
                if (rpcStream is null) {
                    await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken).ConfigureAwait(false);
                    continue;
                }
                // Subscription may start mid-GOP; ask the sender for a keyframe so the
                // decoder can (re)configure immediately instead of freezing until the
                // next natural one.
                await LiveVideoStreams.RequestKeyFrame(session, streamId.Value, cancellationToken)
                    .ConfigureAwait(false);
                await foreach (var frame in rpcStream.WithCancellation(cancellationToken).ConfigureAwait(false))
                    Feed(frame);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                return;
            }
            catch (Exception e) when (!cancellationToken.IsCancellationRequested) {
                Log.LogWarning(e, "MauiVideoPlayer: GetStream ended, retrying");
                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void Feed(VideoFrame frame)
    {
        // Decode whatever the server forwards. It sends a single spatial layer at a
        // time and can switch which one dynamically; the decoder reconfigures at the
        // keyframe that accompanies each switch. Filtering by a latched LayerId would
        // drop every frame after a switch and freeze the tile.
        _decoder.Decode(frame.Data, frame.IsKeyFrame);
    }

    private void OnDecoded(CVPixelBuffer pixelBuffer)
    {
        Func<byte[], ValueTask>? onFrame;
        lock (_sync)
            onFrame = _onFrame;
        if (onFrame is not null)
            _emitter.TryEmit(pixelBuffer, onFrame);
    }
}

public sealed class MauiVideoPlayerFactory : INativeVideoPlayerFactory
{
    public INativeVideoPlayer Create(AppUIHub hub, VideoStreamInfo streamInfo)
        // The native-overlay renderer (AVSampleBufferDisplayLayer) is used when its host is
        // registered; otherwise fall back to the JPEG-over-interop canvas player.
        => hub.Services.GetService<NativeVideoOverlayHost>() is { } overlayHost
            ? new MauiVideoLayerPlayer(hub, streamInfo, overlayHost)
            : new MauiVideoPlayer(hub, streamInfo);
}
