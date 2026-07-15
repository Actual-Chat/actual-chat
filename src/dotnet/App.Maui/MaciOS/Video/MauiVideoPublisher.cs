using System.Threading.Channels;
using ActualChat.Media;
using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.App.Services.Video;
using ActualChat.UI.Blazor.Services;
using ActualChat.Video;
using CoreMedia;
using CoreVideo;
using ActualLab.Rpc;

namespace ActualChat.App.Maui.Video;

/// <summary>
/// Native camera → VideoToolbox encode → <c>ILiveVideoStreams.PushStream</c> pipeline
/// for Apple platforms. Drop-in replacement for the JS recorder's publish path on
/// Mac Catalyst (where WebCodecs is unavailable). Camera only; ScreenCast stays on JS.
/// </summary>
public sealed class MauiVideoPublisher : INativeVideoPublisher
{
    private const double FrameRate = 30;
    private const int FloodClose = 15;
    private const int FloodOpen = 6;
    private const string DefaultCodec = "avc1.640028"; // H.264 High 4.0

    private readonly AppUIHub _hub;
    private readonly VideoSourceKind _kind;
    private readonly INativeVideoPublisherCallbacks _callbacks;
    private readonly Lock _sync = new();
    private readonly Lock _bundleLock = new();

    private readonly AppleVideoCapture _capture;
    private readonly AppleCameraFrameTap? _tap;
    private readonly List<AppleVideoEncoder> _encoders = new();
    private readonly Dictionary<long, BundleAccumulator> _pending = new();
    private readonly Dictionary<long, (int Index, int Expected)> _ptsInfo = new();

    private VideoLayerDef[] _ladder = [];
    private int[] _lastKeyFrameIndexByLayer = [];
    private string _codec = DefaultCodec;
    private string _deviceId = "";
    private ChatId _chatId;
    private IReadOnlyList<string> _warmCodecs = [];

    private Channel<VideoFrameBundle>? _outbound;
    private int _outboundCount;
    private bool _gateSkipping;
    private bool _gateOpen;
    private bool _capturing;
    private int _pendingForceKeyFrame;
    private int _frameCounter;
    private long _lastKeyFrameAtMs;
    private int _sourceIndex;
    private bool _loggedFirstCapture;
    private double? _firstPtsSeconds;
    private double _sourceStartedAtSeconds;
    private CancellationTokenSource? _sendCts;
    private Task? _sendTask;

    private ILiveVideoStreams LiveVideoStreams => field ??= _hub.Services.GetRequiredService<ILiveVideoStreams>();
    private ConnectivityUI ConnectivityUI => field ??= _hub.Services.GetRequiredService<ConnectivityUI>();
    private ILogger Log => field ??= _hub.Services.LogFor<MauiVideoPublisher>();

    public MauiVideoPublisher(AppUIHub hub, VideoSourceKind kind, INativeVideoPublisherCallbacks callbacks)
    {
        _hub = hub;
        _kind = kind;
        _callbacks = callbacks;
        _capture = new AppleVideoCapture(hub.Services.LogFor<AppleVideoCapture>());
        _capture.FrameCaptured += OnCaptured;
        _tap = hub.Services.GetService<AppleCameraFrameTap>();
    }

    public Task Warmup(ChatId chatId, IReadOnlyList<string> supportedCodecs, CancellationToken cancellationToken)
    {
        // Warmup does NOT open the camera on Apple: the preview modal shows an
        // independent getUserMedia stream (WebCodecs-free), so opening the native
        // AVCaptureSession here would just double-grab the camera. Native capture
        // starts at OpenGate/StartRecording (i.e. when the stream actually goes live).
        _chatId = chatId;
        _warmCodecs = supportedCodecs;
        return Task.CompletedTask;
    }

    public Task StartRecording(ChatId chatId, IReadOnlyList<string> supportedCodecs, int maxLayerCount, CancellationToken cancellationToken)
    {
        _chatId = chatId;
        StartCapture(supportedCodecs, maxLayerCount);
        OpenGateInternal();
        return Task.CompletedTask;
    }

    public Task OpenGate(int maxLayerCount, CancellationToken cancellationToken)
    {
        StartCapture(_warmCodecs, maxLayerCount);
        OpenGateInternal();
        return Task.CompletedTask;
    }

    public Task CancelWarmup(CancellationToken cancellationToken)
    {
        Teardown(raiseStopped: false);
        return Task.CompletedTask;
    }

    public Task StopRecording(CancellationToken cancellationToken)
    {
        Teardown(raiseStopped: true);
        return Task.CompletedTask;
    }

    public Task StartScreenCast(ChatId chatId, IReadOnlyList<string> supportedCodecs, int maxLayerCount, CancellationToken cancellationToken)
        => throw StandardError.NotSupported("Native screen capture is not implemented; ScreenCast stays on the JS recorder.");

    public Task SetSelectedCamera(string deviceId, CancellationToken cancellationToken)
    {
        _deviceId = deviceId;
        if (_capturing && _capture.SwitchTo(deviceId))
            RaiseTrackSettings();
        return Task.CompletedTask;
    }

    public Task SwitchCamera(string deviceId, CancellationToken cancellationToken)
        => SetSelectedCamera(deviceId, cancellationToken);

    public Task SetBlurEnabled(bool enabled, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task ToggleBlur(bool enabled, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task SetLayers(IReadOnlyList<VideoLayerDef>? layers, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task SetFpsCeiling(int maxFps, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task ForceKeyFrame(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _pendingForceKeyFrame, 1);
        return Task.CompletedTask;
    }

    public Task SetDemandedLayers(int mask, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task SetThumbnailOnly(bool thumbnailOnly, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task SetSpeaking(bool speaking, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task SetRemoteStreamCount(int count, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task UpdateSupportedDecoderCodecs(IReadOnlyList<string> codecs, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        Teardown(raiseStopped: false);
        _capture.FrameCaptured -= OnCaptured;
        _capture.Dispose();
        var sendTask = _sendTask;
        if (sendTask is not null)
            await sendTask.SilentAwait(false);
    }

    // Private methods

    private void StartCapture(IReadOnlyList<string> supportedCodecs, int maxLayerCount)
    {
        lock (_sync) {
            if (_capturing)
                return;

            _codec = DefaultCodec;
            var ladder = VideoLayerDef.ListFor(_kind);
            var activeCount = Math.Clamp(maxLayerCount, 1, ladder.Length);
            _ladder = ladder;
            _lastKeyFrameIndexByLayer = new int[ladder.Length];
            Array.Fill(_lastKeyFrameIndexByLayer, -1);

            if (!_capture.Start(_deviceId)) {
                _callbacks.OnRecordingError("Camera is unavailable");
                return;
            }

            for (byte layerId = 0; layerId < activeCount; layerId++) {
                var layer = ladder[layerId];
                var bitrate = (int)Math.Ceiling(layer.GetBitrateKbps(_codec) * 1000);
                var encoder = new AppleVideoEncoder(
                    layerId, layer.Width, layer.Height, bitrate, FrameRate, _codec,
                    _hub.Services.LogFor<AppleVideoEncoder>());
                encoder.Encoded += OnEncoded;
                _encoders.Add(encoder);
            }

            _outbound = Channel.CreateUnbounded<VideoFrameBundle>(new UnboundedChannelOptions {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });
            _outboundCount = 0;
            _frameCounter = 0;
            _sourceIndex = -1;
            _firstPtsSeconds = null;
            _lastKeyFrameAtMs = 0;
            _sourceStartedAtSeconds = _hub.Clocks.ServerClock.Now.EpochOffset.TotalSeconds;
            _capturing = true;
        }
        _tap?.SetSource(true);
    }

    private void OpenGateInternal()
    {
        lock (_sync) {
            if (!_capturing || _gateOpen)
                return;

            _gateOpen = true;
            Interlocked.Exchange(ref _pendingForceKeyFrame, 1);
            _sendCts = new CancellationTokenSource();
            _sendTask = BackgroundTask.Run(() => SendLoop(_sendCts.Token), Log, "MauiVideoPublisher.SendLoop failed");
        }
        _callbacks.OnRecordingStarted();
    }

    private void Teardown(bool raiseStopped)
    {
        List<AppleVideoEncoder> encoders;
        CancellationTokenSource? sendCts;
        Channel<VideoFrameBundle>? outbound;
        lock (_sync) {
            if (!_capturing && !_gateOpen) {
                if (raiseStopped)
                    _callbacks.OnRecordingStopped();
                return;
            }
            _gateOpen = false;
            _capturing = false;
            sendCts = _sendCts;
            _sendCts = null;
            outbound = _outbound;
            _outbound = null;
            encoders = new List<AppleVideoEncoder>(_encoders);
            _encoders.Clear();
        }

        sendCts?.CancelAndDisposeSilently();
        outbound?.Writer.TryComplete();
        _tap?.SetSource(false);
        _capture.Stop();
        foreach (var encoder in encoders) {
            encoder.Encoded -= OnEncoded;
            encoder.Dispose();
        }
        lock (_bundleLock) {
            _pending.Clear();
            _ptsInfo.Clear();
        }
        if (raiseStopped)
            _callbacks.OnRecordingStopped();
    }

    private void OnCaptured(CMSampleBuffer sampleBuffer, CMTime pts)
    {
        _tap?.Publish(sampleBuffer, pts);
        if (!_capturing)
            return;

        var imageBuffer = sampleBuffer.GetImageBuffer();
        if (imageBuffer is null)
            return;

        if (!_loggedFirstCapture) {
            _loggedFirstCapture = true;
            NativeVideoTrace.Log($"publisher: first captured frame (gateOpen={_gateOpen})");
        }
        var duration = new CMTime((long)(FrameRate == 0 ? 0 : 1), (int)FrameRate);
        if (!_gateOpen) {
            // Keep encoders warm while the wire gate is closed.
            foreach (var encoder in _encoders)
                encoder.Encode(imageBuffer, pts, duration, false);
            return;
        }

        if (ShouldSkipForFloodGate())
            return;

        var forceKeyFrame = ComputeForceKeyFrame();
        var index = Interlocked.Increment(ref _sourceIndex);
        lock (_bundleLock) {
            _ptsInfo[pts.Value] = (index, _encoders.Count);
        }
        foreach (var encoder in _encoders)
            encoder.Encode(imageBuffer, pts, duration, forceKeyFrame);
    }

    private void OnEncoded(NativeEncodedFrame frame)
    {
        VideoFrameBundle? bundle = null;
        lock (_bundleLock) {
            if (!_gateOpen)
                return;
            if (!_ptsInfo.TryGetValue(frame.Pts.Value, out var info))
                return;

            if (!_pending.TryGetValue(frame.Pts.Value, out var accumulator)) {
                accumulator = new BundleAccumulator(info.Index, info.Expected, frame.Pts);
                _pending[frame.Pts.Value] = accumulator;
            }
            accumulator.Layers.Add(frame);
            if (accumulator.Layers.Count < accumulator.Expected)
                return;

            bundle = BuildBundle(accumulator);
            _pending.Remove(frame.Pts.Value);
            _ptsInfo.Remove(frame.Pts.Value);
            PruneOlderThan(accumulator.Index);
        }

        if (bundle is not null)
            Enqueue(bundle);
    }

    private VideoFrameBundle BuildBundle(BundleAccumulator accumulator)
    {
        _firstPtsSeconds ??= accumulator.Pts.Seconds;
        var offsetSeconds = accumulator.Pts.Seconds - _firstPtsSeconds.Value;
        var offset = TimeSpan.FromTicks((long)Math.Round(offsetSeconds * TimeSpan.TicksPerSecond));
        var duration = TimeSpan.FromTicks((long)Math.Round(TimeSpan.TicksPerSecond / FrameRate));
        var layerCount = (byte)_ladder.Length;

        var ordered = accumulator.Layers.OrderBy(f => f.LayerId).ToArray();
        var layerMask = 0;
        foreach (var f in ordered)
            layerMask |= 1 << f.LayerId;

        var frames = new VideoFrame[ordered.Length];
        for (var i = 0; i < ordered.Length; i++) {
            var f = ordered[i];
            if (f.IsKeyFrame)
                _lastKeyFrameIndexByLayer[f.LayerId] = accumulator.Index;
            var keyFrameIndex = _lastKeyFrameIndexByLayer[f.LayerId];
            frames[i] = new VideoFrame {
                Data = f.Data,
                Offset = offset,
                Duration = duration,
                Index = accumulator.Index,
                KeyFrameIndex = keyFrameIndex,
                Width = f.IsKeyFrame ? f.Width : 0,
                Height = f.IsKeyFrame ? f.Height : 0,
                Codec = f.IsKeyFrame ? _codec : null,
                Description = f.IsKeyFrame && f.Description is not null ? f.Description : default,
                LayerId = f.LayerId,
                LayerCount = layerCount,
                LayerMask = (byte)layerMask,
            };
        }
        return new VideoFrameBundle(frames);
    }

    private void PruneOlderThan(int index)
    {
        if (_pending.Count == 0)
            return;

        var stale = _pending.Where(kv => kv.Value.Index < index).Select(kv => kv.Key).ToArray();
        foreach (var key in stale) {
            _pending.Remove(key);
            _ptsInfo.Remove(key);
        }
    }

    private void Enqueue(VideoFrameBundle bundle)
    {
        var outbound = _outbound;
        if (outbound is null)
            return;

        if (outbound.Writer.TryWrite(bundle))
            Interlocked.Increment(ref _outboundCount);
    }

    private bool ShouldSkipForFloodGate()
    {
        var count = Volatile.Read(ref _outboundCount);
        if (_gateSkipping) {
            if (count <= FloodOpen)
                _gateSkipping = false;
            else
                return true;
        }
        else if (count >= FloodClose) {
            _gateSkipping = true;
            return true;
        }
        return false;
    }

    private bool ComputeForceKeyFrame()
    {
        var force = Interlocked.Exchange(ref _pendingForceKeyFrame, 0) == 1;
        _frameCounter++;
        var keyframeIntervalFrames = (int)(FrameRate * 3);
        if (keyframeIntervalFrames > 0 && _frameCounter % keyframeIntervalFrames == 0)
            force = true;
        var nowMs = Environment.TickCount64;
        if (_lastKeyFrameAtMs != 0 && nowMs - _lastKeyFrameAtMs >= 5000)
            force = true;
        if (force)
            _lastKeyFrameAtMs = nowMs;
        return force;
    }

    private async Task SendLoop(CancellationToken cancellationToken)
    {
        var session = _hub.Session;
        NativeVideoTrace.Log("publisher: SendLoop started");
        while (!cancellationToken.IsCancellationRequested) {
            await ConnectivityUI.WhenConnected(cancellationToken).ConfigureAwait(false);
            Interlocked.Exchange(ref _pendingForceKeyFrame, 1);
            NativeVideoTrace.Log("publisher: connected, waiting for first keyframe bundle");

            var firstBundle = await ReadUntilKeyframe(cancellationToken).ConfigureAwait(false);
            if (firstBundle is null) {
                NativeVideoTrace.Log("publisher: ReadUntilKeyframe returned null (channel closed)");
                return;
            }

            var format = BuildFormat(firstBundle);
            NativeVideoTrace.Log($"publisher: got keyframe → PushStream {format.Codec} {format.Size.Width}x{format.Size.Height}");
            var frameStream = BuildFrames(firstBundle, cancellationToken).SuppressCancellation(cancellationToken);
            try {
                var rpcStream = RpcStream.New(frameStream);
                await LiveVideoStreams.PushStream(
                        session, _chatId.Value, _sourceStartedAtSeconds, format, _kind, rpcStream, cancellationToken)
                    .ConfigureAwait(false);
                NativeVideoTrace.Log("publisher: PushStream returned normally");
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            }
            catch (Exception e) when (!cancellationToken.IsCancellationRequested) {
                NativeVideoTrace.Log($"publisher: PushStream FAILED {e.GetType().Name}: {e.Message}");
                Log.LogWarning(e, "PushStream ended, retrying with a new call");
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<VideoFrameBundle?> ReadUntilKeyframe(CancellationToken cancellationToken)
    {
        var outbound = _outbound;
        if (outbound is null)
            return null;

        while (await outbound.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false)) {
            while (outbound.Reader.TryRead(out var bundle)) {
                Interlocked.Decrement(ref _outboundCount);
                if (bundle.IsKeyFrame)
                    return bundle;
            }
        }
        return null;
    }

    private async IAsyncEnumerable<VideoFrameBundle> BuildFrames(
        VideoFrameBundle first,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return first;
        NativeVideoTrace.Log("publisher: yielded first bundle into RPC stream");
        var sent = 1;

        var outbound = _outbound;
        if (outbound is null)
            yield break;

        while (await outbound.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false)) {
            while (outbound.Reader.TryRead(out var bundle)) {
                Interlocked.Decrement(ref _outboundCount);
                sent++;
                if (sent % 60 == 0)
                    NativeVideoTrace.Log($"publisher: yielded {sent} bundles into RPC stream");
                yield return bundle;
            }
        }
    }

    private VideoFormat BuildFormat(VideoFrameBundle bundle)
    {
        var top = bundle.TopLayer;
        return new VideoFormat {
            Codec = top.Codec ?? _codec,
            CodecSettings = top.Description.IsEmpty ? "" : Convert.ToBase64String(top.Description.Span),
            LayerId = top.LayerId,
            Size = new Size2D(top.Width, top.Height),
            SourceSize = new Size2D(_capture.SourceWidth, _capture.SourceHeight),
        };
    }

    private void RaiseTrackSettings()
        => _callbacks.OnTrackSettings(_capture.CurrentDeviceId, null);

    // Nested types

    private sealed class BundleAccumulator(int index, int expected, CMTime pts)
    {
        public int Index { get; } = index;
        public int Expected { get; } = expected;
        public CMTime Pts { get; } = pts;
        public List<NativeEncodedFrame> Layers { get; } = new();
    }
}
