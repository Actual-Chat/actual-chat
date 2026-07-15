using CoreGraphics;
using CoreImage;
using CoreVideo;
using UIKit;

namespace ActualChat.App.Maui.Video;

/// <summary>
/// Downscales a <see cref="CVPixelBuffer"/> (captured or decoded) to a JPEG for canvas
/// rendering in the WKWebView. Shared by <see cref="AppleCameraPreview"/> and
/// <see cref="MauiVideoPlayer"/>, both of which push base64 JPEG frames to a JS canvas.
/// </summary>
public sealed class AppleJpegEncoder : IDisposable
{
    private readonly CIContext _ciContext = CIContext.Create();

    public byte[]? EncodeJpeg(CVPixelBuffer pixelBuffer, int targetWidth, float quality)
    {
        using var ciImage = new CIImage(pixelBuffer);
        var extent = ciImage.Extent;
        if (extent.Width <= 0 || extent.Height <= 0)
            return null;

        var scale = targetWidth / extent.Width;
        if (scale > 1)
            scale = 1;
        using var scaled = ciImage.ImageByApplyingTransform(CGAffineTransform.MakeScale(scale, scale));
        using var cgImage = _ciContext.CreateCGImage(scaled, scaled.Extent);
        if (cgImage is null)
            return null;

        using var uiImage = new UIImage(cgImage);
        using var jpeg = uiImage.AsJPEG(quality);
        return jpeg?.ToArray();
    }

    public void Dispose()
        => _ciContext.Dispose();
}
