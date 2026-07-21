using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.App.Services.Video;
using ActualChat.UI.Blazor.Services;
using ActualChat.Video;

namespace ActualChat.App.Maui.Video;

/// <summary>
/// Native <c>ILiveVideoStreams.GetStream</c> → VideoToolbox → <c>AVSampleBufferDisplayLayer</c>
/// pipeline for one remote tile on Mac Catalyst. Unlike <see cref="MauiVideoPlayer"/> (which
/// decodes to JPEG and pushes over interop), this feeds the compressed sample buffers straight
/// into a native layer positioned over the WKWebView — no per-frame interop, full resolution.
/// H.264 only.
/// </summary>
public sealed class MauiVideoLayerPlayer : INativeOverlayVideoPlayer
{
    private readonly AppUIHub _hub;
    private readonly VideoStreamInfo _streamInfo;
    private readonly NativeVideoOverlayHost _overlayHost;
    private readonly SampleBufferDisplayView _view;
    private readonly H264SampleBufferBuilder _builder;
    private readonly Lock _sync = new();

    private CancellationTokenSource? _cts;
    private Task? _pullTask;
    private volatile bool _decoderFailed;

    public string OverlayId => _streamInfo.StreamId.Value;

    private ILiveVideoStreams LiveVideoStreams => field ??= _hub.Services.GetRequiredService<ILiveVideoStreams>();
    private ConnectivityUI ConnectivityUI => field ??= _hub.Services.GetRequiredService<ConnectivityUI>();
    private ILogger Log => field ??= _hub.Services.LogFor(GetType());

    public MauiVideoLayerPlayer(AppUIHub hub, VideoStreamInfo streamInfo, NativeVideoOverlayHost overlayHost)
    {
        _hub = hub;
        _streamInfo = streamInfo;
        _overlayHost = overlayHost;
        _view = new SampleBufferDisplayView();
        _view.DecodingFailed += OnDecodingFailed;
        _builder = new H264SampleBufferBuilder(hub.Services.LogFor<MauiVideoLayerPlayer>());
        _overlayHost.Attach(OverlayId, _view);
    }

    public void Start(Func<byte[], ValueTask> onFrame)
    {
        // onFrame is unused: this player renders into the native overlay layer, not a canvas.
        lock (_sync) {
            if (_pullTask is not null)
                return;

            _cts = new CancellationTokenSource();
            _pullTask = BackgroundTask.Run(() => PullLoop(_cts.Token), Log, "MauiVideoLayerPlayer.PullLoop failed");
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        lock (_sync) {
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
        _overlayHost.Remove(OverlayId);
        _view.DecodingFailed -= OnDecodingFailed;
        MainThread.BeginInvokeOnMainThread(_view.Dispose);
        _builder.Dispose();
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

                // Subscription may start mid-GOP; ask the sender for a keyframe so the layer
                // can configure immediately instead of freezing until the next natural one.
                await LiveVideoStreams.RequestKeyFrame(session, streamId.Value, cancellationToken)
                    .ConfigureAwait(false);
                var waitingForKeyFrame = false;
                await foreach (var frame in rpcStream.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                    if (_decoderFailed) {
                        _decoderFailed = false;
                        _ = LiveVideoStreams.RequestKeyFrame(session, streamId.Value, cancellationToken);
                        waitingForKeyFrame = true;
                    }
                    if (waitingForKeyFrame) {
                        if (!frame.IsKeyFrame)
                            continue;

                        waitingForKeyFrame = false;
                    }
                    Feed(frame);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                return;
            }
            catch (Exception e) when (!cancellationToken.IsCancellationRequested) {
                Log.LogWarning(e, "MauiVideoLayerPlayer: GetStream ended, retrying");
                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void Feed(VideoFrame frame)
    {
        // The server sends a single spatial layer at a time and can switch dynamically; the
        // builder re-latches the format at the keyframe accompanying each switch, and we flush
        // the layer's decoder at that boundary so it reconfigures cleanly.
        using var sampleBuffer = _builder.TryBuild(frame.Data.Span, frame.IsKeyFrame, out var formatChanged);
        if (sampleBuffer is null)
            return;

        if (formatChanged)
            _view.Flush();
        _view.Enqueue(sampleBuffer);
    }

    private void OnDecodingFailed()
        => _decoderFailed = true;
}
