using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Memory;
using ImageSharpConfiguration = SixLabors.ImageSharp.Configuration;

namespace ActualChat.Uploads;

/// <summary>
/// Size limits every uploaded raster image is checked against,
/// plus the ImageSharp configuration and decoder options used to read them.
/// </summary>
public static class ImageLimits
{
    // 50 MP covers any current phone camera - a 48 MP sensor produces 8000x6000 = 48 MP.
    public const long MaxPixelCount = 50_000_000;
    // Sum over all frames of an animated image; ImageSharp materializes 4 bytes per pixel per frame.
    public const long MaxTotalPixelCount = 2 * MaxPixelCount;
    public const int MaxFrameCount = 1000;
    public const int MaxAllocationMegabytes = 512;
    public static readonly ImageSharpConfiguration Configuration = NewConfiguration();
    public static readonly DecoderOptions DecoderOptions = new() {
        Configuration = Configuration,
        // One above the limit, so that an image with too many frames is still counted as such
        MaxFrames = MaxFrameCount + 1,
    };

    public static ImageInfo RequireWithinLimits(this ImageInfo imageInfo)
    {
        var pixelCount = (long)imageInfo.Width * imageInfo.Height;
        if (pixelCount > MaxPixelCount)
            throw StandardError.Constraint($"Image is too big: {imageInfo.Width}x{imageInfo.Height}.");

        var frameCount = imageInfo.FrameMetadataCollection.Count;
        if (frameCount > MaxFrameCount)
            throw StandardError.Constraint($"Image has too many frames: {frameCount}.");

        return imageInfo;
    }

    public static ImageInfo RequireDecodable(this ImageInfo imageInfo)
    {
        imageInfo.RequireWithinLimits();
        var frameCount = Math.Max(1, imageInfo.FrameMetadataCollection.Count);
        var totalPixelCount = (long)imageInfo.Width * imageInfo.Height * frameCount;
        if (totalPixelCount > MaxTotalPixelCount)
            throw StandardError.Constraint(
                $"Image is too big: {imageInfo.Width}x{imageInfo.Height}, {frameCount} frame(s).");

        return imageInfo;
    }

    // Private methods

    private static ImageSharpConfiguration NewConfiguration()
    {
        var result = ImageSharpConfiguration.Default.Clone();
        result.MemoryAllocator = MemoryAllocator.Create(new MemoryAllocatorOptions {
            AllocationLimitMegabytes = MaxAllocationMegabytes,
        });
        return result;
    }
}
