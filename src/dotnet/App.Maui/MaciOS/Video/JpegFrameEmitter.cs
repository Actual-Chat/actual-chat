using CoreGraphics;
using CoreImage;
using CoreVideo;

namespace ActualChat.App.Maui.Video;

/// <summary>
/// Downscales a <see cref="CVPixelBuffer"/> (captured or decoded) to a JPEG and pushes it
/// to an emit callback, dropping frames while a previous emit is still in flight. Shared by
/// <see cref="AppleCameraPreview"/> and <see cref="MauiVideoPlayer"/>, both of which feed
/// base64 JPEG frames to a JS canvas.
/// </summary>
public sealed class JpegFrameEmitter(int targetWidth, float quality, ILogger log) : IDisposable
{
    private readonly CIContext _ciContext = CIContext.Create();
    private readonly CGColorSpace _colorSpace = CGColorSpace.CreateSrgb()!;
    private int _emitInFlight;

    public void TryEmit(CVPixelBuffer pixelBuffer, Func<byte[], ValueTask> onFrame)
    {
        // Drop the frame if a previous emit is still crossing the JS interop hop —
        // frames must never queue behind a slow render. EncodeJpeg copies the pixels
        // into a standalone byte[] synchronously, so the caller may release
        // pixelBuffer as soon as this returns; the emit task only touches that byte[].
        if (Interlocked.CompareExchange(ref _emitInFlight, 1, 0) != 0)
            return;

        byte[]? jpeg = null;
        try {
            jpeg = EncodeJpeg(pixelBuffer);
        }
        catch (Exception e) {
            log.LogWarning(e, "JpegFrameEmitter: frame encode failed");
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

    public void Dispose()
    {
        _ciContext.Dispose();
        _colorSpace.Dispose();
    }

    // Private methods

    private byte[]? EncodeJpeg(CVPixelBuffer pixelBuffer)
    {
        using var ciImage = new CIImage(pixelBuffer);
        var extent = ciImage.Extent;
        if (extent.Width <= 0 || extent.Height <= 0)
            return null;

        var scale = targetWidth / extent.Width;
        if (scale > 1)
            scale = 1;
        using var scaled = ciImage.ImageByApplyingTransform(CGAffineTransform.MakeScale(scale, scale));
        var options = new CIImageRepresentationOptions { LossyCompressionQuality = quality };
        using var jpeg = _ciContext.GetJpegRepresentation(scaled, _colorSpace, options);
        return jpeg?.ToArray();
    }
}
