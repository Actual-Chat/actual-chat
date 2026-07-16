using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.App.Services.Video;
using CoreMedia;
using CoreVideo;

namespace ActualChat.App.Maui.Video;

/// <summary>
/// Native camera preview for Mac Catalyst (WKWebView getUserMedia delivers no frames):
/// throttled, downscaled JPEG frames pushed to the caller for canvas rendering.
/// Taps <see cref="AppleCameraFrameTap"/> while the publisher's capture is live; opens an
/// own <see cref="AppleVideoCapture"/> only pre-publish (JoinVideoCallModal) — a second
/// AVCaptureSession on the same camera starves the publisher's session on macOS.
/// </summary>
public sealed class AppleCameraPreview : INativeCameraPreview
{
    private const int TargetWidth = 360;
    private const float JpegQuality = 0.6f;
    private const double MinFrameIntervalSeconds = 1.0 / 15;

    private readonly ILogger _log;
    private readonly AppleCameraFrameTap? _tap;
    private readonly JpegFrameEmitter _emitter;
    private readonly Lock _sync = new();

    private AppleVideoCapture? _ownCapture;
    private Func<byte[], ValueTask>? _onFrame;
    private double _lastEmitSeconds;

    public AppleCameraPreview(AppleCameraFrameTap? tap, ILogger log)
    {
        _log = log;
        _tap = tap;
        _emitter = new JpegFrameEmitter(TargetWidth, JpegQuality, log);
        if (_tap is not null) {
            _tap.FrameCaptured += OnFrameCaptured;
            _tap.SourceChanged += OnTapSourceChanged;
        }
    }

    public bool Start(string? deviceId, Func<byte[], ValueTask> onFrame)
    {
        lock (_sync) {
            _onFrame = onFrame;
            _lastEmitSeconds = 0;
            if (_tap?.HasSource == true)
                return true;

            if (_ownCapture is null) {
                _ownCapture = new AppleVideoCapture(_log);
                _ownCapture.FrameCaptured += OnFrameCaptured;
            }
            return _ownCapture.Start(deviceId);
        }
    }

    public bool SwitchTo(string? deviceId)
    {
        lock (_sync)
            return _ownCapture?.SwitchTo(deviceId) ?? false;
    }

    public void Stop()
    {
        AppleVideoCapture? ownCapture;
        lock (_sync) {
            _onFrame = null;
            ownCapture = _ownCapture;
            _ownCapture = null;
        }
        DisposeOwnCapture(ownCapture);
    }

    public ValueTask DisposeAsync()
    {
        if (_tap is not null) {
            _tap.FrameCaptured -= OnFrameCaptured;
            _tap.SourceChanged -= OnTapSourceChanged;
        }
        Stop();
        _emitter.Dispose();
        return ValueTask.CompletedTask;
    }

    // Private methods

    private void OnTapSourceChanged(bool hasSource)
    {
        // The publisher's capture came up — release the own session so the two never
        // contend for the camera. Not restarted on source loss: the self-tile unmounts
        // right after recording stops, and reopening would contend on the next start.
        if (!hasSource)
            return;

        AppleVideoCapture? ownCapture;
        lock (_sync) {
            ownCapture = _ownCapture;
            _ownCapture = null;
        }
        DisposeOwnCapture(ownCapture);
    }

    private void DisposeOwnCapture(AppleVideoCapture? ownCapture)
    {
        if (ownCapture is null)
            return;

        ownCapture.FrameCaptured -= OnFrameCaptured;
        ownCapture.Dispose();
    }

    private void OnFrameCaptured(CMSampleBuffer sampleBuffer, CMTime pts)
    {
        Func<byte[], ValueTask>? onFrame;
        lock (_sync) {
            onFrame = _onFrame;
            if (onFrame is null)
                return;

            var seconds = pts.Seconds;
            if (_lastEmitSeconds != 0 && seconds - _lastEmitSeconds < MinFrameIntervalSeconds)
                return;

            _lastEmitSeconds = seconds;
        }

        if (sampleBuffer.GetImageBuffer() is CVPixelBuffer pixelBuffer)
            _emitter.TryEmit(pixelBuffer, onFrame);
    }
}

public sealed class AppleCameraPreviewFactory : INativeCameraPreviewFactory
{
    public INativeCameraPreview Create(AppUIHub hub)
        => new AppleCameraPreview(
            hub.Services.GetService<AppleCameraFrameTap>(),
            hub.Services.LogFor<AppleCameraPreview>());
}
