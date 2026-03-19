using CoreImage;
using CoreVideo;
using Foundation;
using Metal;
using Vision;

namespace ActualChat.App.Maui.Video;

public sealed class IosBackgroundBlur : IDisposable
{
    private const float BlurRadius = 20f;

    private readonly VNGeneratePersonSegmentationRequest _segmentationRequest;
    private readonly CIContext _ciContext;
    private readonly ILogger _log;
    private CVPixelBufferPool? _pixelBufferPool;
    private int _poolWidth;
    private int _poolHeight;

    public IosBackgroundBlur(ILogger log)
    {
        _log = log;
        _segmentationRequest = new VNGeneratePersonSegmentationRequest {
            QualityLevel = VNGeneratePersonSegmentationRequestQualityLevel.Balanced,
            OutputPixelFormat = CVPixelFormatType.CV32F, // single-channel float mask
        };

        var device = MTLDevice.SystemDefault;
        _ciContext = device != null
            ? CIContext.FromMetalDevice(device)
            : new CIContext();
    }

    public CVPixelBuffer? Process(CVPixelBuffer input)
    {
        try {
            // Run person segmentation
            using var handler = new VNImageRequestHandler(input, new NSDictionary());
            handler.Perform(new VNRequest[] { _segmentationRequest }, out var error);
            if (error != null) {
                _log.LogWarning("Vision segmentation failed: {Error}", error.LocalizedDescription);
                return null;
            }

            var result = _segmentationRequest.Results?.FirstOrDefault() as VNPixelBufferObservation;
            if (result?.PixelBuffer == null)
                return null;

            var maskBuffer = result.PixelBuffer;

            // Build CIImages
            var sourceImage = CIImage.FromImageBuffer(input);
            var maskImage = CIImage.FromImageBuffer(maskBuffer);

            // Scale mask to match source dimensions
            var scaleX = sourceImage.Extent.Width / maskImage.Extent.Width;
            var scaleY = sourceImage.Extent.Height / maskImage.Extent.Height;
            var scaledMask = maskImage.ImageByApplyingTransform(
                CoreGraphics.CGAffineTransform.MakeScale(scaleX, scaleY));

            // Create blurred background
            var blurFilter = CIFilter.FromName("CIGaussianBlur");
            blurFilter!.SetValueForKey(sourceImage, CIFilterInputKey.Image);
            blurFilter.SetValueForKey(NSNumber.FromFloat(BlurRadius), CIFilterInputKey.Radius);
            var blurredBackground = blurFilter.OutputImage;
            if (blurredBackground == null)
                return null;

            // Crop blurred image to source extent (CIGaussianBlur expands bounds)
            blurredBackground = blurredBackground.ImageByCroppingToRect(sourceImage.Extent);

            // Blend: person from source, background from blurred, using mask
            var blendFilter = CIFilter.FromName("CIBlendWithMask");
            blendFilter!.SetValueForKey(sourceImage, CIFilterInputKey.Image);
            blendFilter.SetValueForKey(blurredBackground, CIFilterInputKey.BackgroundImage);
            blendFilter.SetValueForKey(scaledMask, CIFilterInputKey.MaskImage);
            var composited = blendFilter.OutputImage;
            if (composited == null)
                return null;

            // Render to output CVPixelBuffer
            var width = (int)input.Width;
            var height = (int)input.Height;
            var outputBuffer = GetOutputBuffer(width, height, input.PixelFormatType);
            if (outputBuffer == null)
                return null;

            _ciContext.Render(composited, outputBuffer);
            return outputBuffer;
        }
        catch (Exception e) {
            _log.LogWarning(e, "Background blur processing failed");
            return null;
        }
    }

    private CVPixelBuffer? GetOutputBuffer(int width, int height, CVPixelFormatType pixelFormat)
    {
        if (_pixelBufferPool == null || _poolWidth != width || _poolHeight != height) {
            _pixelBufferPool?.Dispose();
            var poolAttrs = new CVPixelBufferPoolSettings {
                MinimumBufferCount = 3,
            };
            var bufferAttrs = new CVPixelBufferAttributes {
                Width = width,
                Height = height,
                PixelFormatType = pixelFormat,
            };
            _pixelBufferPool = new CVPixelBufferPool(poolAttrs, bufferAttrs);
            _poolWidth = width;
            _poolHeight = height;
        }

        var status = _pixelBufferPool.CreatePixelBuffer(out var buffer);
        if (status != CVReturn.Success || buffer == null) {
            _log.LogWarning("Failed to create pixel buffer from pool: {Status}", status);
            return null;
        }
        return buffer;
    }

    public void Dispose()
    {
        _pixelBufferPool?.Dispose();
        _ciContext.Dispose();
        _segmentationRequest.Dispose();
    }
}
