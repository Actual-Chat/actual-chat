using SkiaSharp;
using Svg.Skia;

namespace ActualChat.Uploads;

/// <summary>
/// Rasterizes SVG vector images to PNG via SkiaSharp.
/// Because SVG is resolution-independent, the output is always rendered at <c>size</c>
/// along its larger dimension (preserving aspect ratio), regardless of any
/// <c>width</c>/<c>height</c> declared in the SVG source.
/// </summary>
public class SvgRasterizer(ILogger<SvgRasterizer> log)
{
    public Size RasterizeToPng(Stream svgStream, Stream pngStream, int size)
    {
        var inputLength = svgStream.CanSeek ? svgStream.Length : -1;
        log.LogDebug("Rasterizing SVG ({InputLength} bytes) to PNG (target size: {Size})", inputLength, size);

        using var svg = SKSvg.CreateFromStream(svgStream);
        var picture = svg.Picture
            ?? throw StandardError.Internal("Failed to parse SVG file.");

        var bounds = picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw StandardError.Internal("SVG has invalid dimensions.");

        // Vector source: scale so the larger dimension equals size, preserving aspect ratio.
        var scale = size / Math.Max(bounds.Width, bounds.Height);
        var targetWidth = (int)Math.Round(bounds.Width * scale);
        var targetHeight = (int)Math.Round(bounds.Height * scale);
        log.LogDebug(
            "SVG bounds {Width}x{Height} -> target {TargetWidth}x{TargetHeight} (scale: {Scale:F2})",
            bounds.Width, bounds.Height, targetWidth, targetHeight, scale);

        var imageInfo = new SKImageInfo(targetWidth, targetHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(imageInfo);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(scale, scale);
        canvas.DrawPicture(picture);

        using var pixmap = surface.PeekPixels();
        using var data = pixmap.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw StandardError.Internal("Failed to encode SVG as PNG.");
        data.SaveTo(pngStream);
        log.LogInformation(
            "Rasterized SVG to PNG ({TargetWidth}x{TargetHeight}, {ByteCount}B)",
            targetWidth, targetHeight, data.Size);

        return new Size(targetWidth, targetHeight);
    }

    public (MemoryStream Stream, Size Size) RasterizeToPng(Stream svgStream, int size)
    {
        var pngStream = new MemoryStream();
        var pngSize = RasterizeToPng(svgStream, pngStream, size);
        pngStream.Position = 0;
        return (pngStream, pngSize);
    }
}
