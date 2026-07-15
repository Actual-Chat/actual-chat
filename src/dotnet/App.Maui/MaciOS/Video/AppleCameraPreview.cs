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
    private const double MinFrameIntervalSeconds = 1.0 / 15;

    private readonly ILogger _log;
    private readonly AppleCameraFrameTap? _tap;
    private readonly AppleJpegEncoder _jpegEncoder = new();
    private readonly Lock _sync = new();

    private AppleVideoCapture? _ownCapture;
    private Func<byte[], ValueTask>? _onFrame;
    private double _lastEmitSeconds;
    private int _emitInFlight;

    public AppleCameraPreview(AppleCameraFrameTap? tap, ILogger log)
    {
        _log = log;
        _tap = tap;
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
        _jpegEncoder.Dispose();
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

        // Drop the frame if a previous emit is still in flight — preview is
        // best-effort and must not queue frames behind a slow JS interop hop.
        if (Interlocked.CompareExchange(ref _emitInFlight, 1, 0) != 0)
            return;

        // EncodeJpeg copies the pixels into a standalone byte[] synchronously, so the
        // capture delegate can dispose sampleBuffer as soon as this returns; the emit
        // task below only touches that byte[]. The in-flight flag clears when it ends.
        byte[]? jpeg = null;
        try {
            if (sampleBuffer.GetImageBuffer() is CVPixelBuffer pixelBuffer)
                jpeg = _jpegEncoder.EncodeJpeg(pixelBuffer, TargetWidth, 0.6f);
        }
        catch (Exception e) {
            _log.LogWarning(e, "AppleCameraPreview: frame encode failed");
        }
        if (jpeg is null) {
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

public sealed class AppleCameraPreviewFactory : INativeCameraPreviewFactory
{
    public INativeCameraPreview Create(AppUIHub hub)
        => new AppleCameraPreview(
            hub.Services.GetService<AppleCameraFrameTap>(),
            hub.Services.LogFor<AppleCameraPreview>());
}
