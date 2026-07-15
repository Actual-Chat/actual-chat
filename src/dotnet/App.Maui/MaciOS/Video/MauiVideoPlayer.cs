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
    private readonly AppleJpegEncoder _jpegEncoder = new();
    private readonly AppleVideoDecoder _decoder;

    private Func<byte[], ValueTask>? _onFrame;
    private int _emitInFlight;
    private CancellationTokenSource? _cts;
    private Task? _pullTask;

    private ILiveVideoStreams LiveVideoStreams => field ??= _hub.Services.GetRequiredService<ILiveVideoStreams>();
    private ConnectivityUI ConnectivityUI => field ??= _hub.Services.GetRequiredService<ConnectivityUI>();
    private ILogger Log => field ??= _hub.Services.LogFor<MauiVideoPlayer>();

    public MauiVideoPlayer(AppUIHub hub, VideoStreamInfo streamInfo)
    {
        _hub = hub;
        _streamInfo = streamInfo;
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
        _jpegEncoder.Dispose();
    }

    // Private methods

    private async Task PullLoop(CancellationToken cancellationToken)
    {
        var session = _hub.Session;
        var streamId = _streamInfo.StreamId;
        while (!cancellationToken.IsCancellationRequested) {
            try {
                await ConnectivityUI.WhenConnected(cancellationToken).ConfigureAwait(false);
                var rpcStream = await LiveVideoStreams.GetStream(session, streamId, cancellationToken).ConfigureAwait(false);
                if (rpcStream is null) {
                    await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken).ConfigureAwait(false);
                    continue;
                }
                // Subscription may start mid-GOP; ask the sender for a keyframe so the
                // decoder can (re)configure immediately instead of freezing until the
                // next natural one.
                await LiveVideoStreams.RequestKeyFrame(session, streamId.Value, cancellationToken).ConfigureAwait(false);
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
        _decoder.Decode(frame.Data.ToArray(), frame.IsKeyFrame);
    }

    private void OnDecoded(CVPixelBuffer pixelBuffer)
    {
        // Best-effort: drop the frame if a prior JPEG emit is still crossing the JS
        // interop hop, so decoded frames never queue behind a slow render.
        if (Interlocked.CompareExchange(ref _emitInFlight, 1, 0) != 0)
            return;

        Func<byte[], ValueTask>? onFrame;
        lock (_sync)
            onFrame = _onFrame;

        byte[]? jpeg = null;
        try {
            if (onFrame is not null)
                jpeg = _jpegEncoder.EncodeJpeg(pixelBuffer, TargetWidth, JpegQuality);
        }
        catch (Exception e) {
            Log.LogWarning(e, "MauiVideoPlayer: frame encode failed");
        }
        if (onFrame is null || jpeg is null) {
            Interlocked.Exchange(ref _emitInFlight, 0);
            return;
        }

        var emit = onFrame(jpeg);
        if (emit.IsCompleted)
            Interlocked.Exchange(ref _emitInFlight, 0);
        else
            _ = emit.AsTask().ContinueWith(
                _ => Interlocked.Exchange(ref _emitInFlight, 0),
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}

public sealed class MauiVideoPlayerFactory : INativeVideoPlayerFactory
{
    public INativeVideoPlayer Create(AppUIHub hub, VideoStreamInfo streamInfo)
        => new MauiVideoPlayer(hub, streamInfo);
}
