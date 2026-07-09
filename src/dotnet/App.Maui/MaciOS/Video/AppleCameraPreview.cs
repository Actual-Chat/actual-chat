using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.App.Services.Video;
using CoreGraphics;
using CoreImage;
using CoreMedia;
using CoreVideo;
using UIKit;

namespace ActualChat.App.Maui.Video;

/// <summary>
/// Native camera preview for Mac Catalyst, where the WKWebView's getUserMedia
/// delivers no frames. Captures via <see cref="AppleVideoCapture"/> and pushes
/// throttled, downscaled JPEG frames to the caller for canvas rendering.
/// </summary>
public sealed class AppleCameraPreview : INativeCameraPreview
{
    private const int TargetWidth = 360;
    private const double MinFrameIntervalSeconds = 1.0 / 15;

    private readonly ILogger _log;
    private readonly AppleVideoCapture _capture;
    private readonly CIContext _ciContext = CIContext.Create();
    private readonly Lock _sync = new();

    private Func<byte[], ValueTask>? _onFrame;
    private double _lastEmitSeconds;
    private int _emitInFlight;

    public AppleCameraPreview(ILogger log)
    {
        _log = log;
        _capture = new AppleVideoCapture(log);
        _capture.FrameCaptured += OnFrameCaptured;
    }

    public bool Start(string? deviceId, Func<byte[], ValueTask> onFrame)
    {
        lock (_sync) {
            _onFrame = onFrame;
            _lastEmitSeconds = 0;
        }
        return _capture.Start(deviceId);
    }

    public bool SwitchTo(string? deviceId)
        => _capture.SwitchTo(deviceId);

    public void Stop()
    {
        lock (_sync)
            _onFrame = null;
        _capture.Stop();
    }

    public ValueTask DisposeAsync()
    {
        Stop();
        _capture.FrameCaptured -= OnFrameCaptured;
        _capture.Dispose();
        _ciContext.Dispose();
        return ValueTask.CompletedTask;
    }

    // Private methods

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
                jpeg = EncodeJpeg(pixelBuffer);
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

    private byte[]? EncodeJpeg(CVPixelBuffer pixelBuffer)
    {
        using var ciImage = new CIImage(pixelBuffer);
        var extent = ciImage.Extent;
        if (extent.Width <= 0 || extent.Height <= 0)
            return null;

        var scale = TargetWidth / extent.Width;
        using var scaled = ciImage.ImageByApplyingTransform(CGAffineTransform.MakeScale(scale, scale));
        using var cgImage = _ciContext.CreateCGImage(scaled, scaled.Extent);
        if (cgImage is null)
            return null;

        using var uiImage = new UIImage(cgImage);
        using var jpeg = uiImage.AsJPEG(0.6f);
        return jpeg?.ToArray();
    }
}

public sealed class AppleCameraPreviewFactory : INativeCameraPreviewFactory
{
    public INativeCameraPreview Create(AppUIHub hub)
        => new AppleCameraPreview(hub.Services.LogFor<AppleCameraPreview>());
}
