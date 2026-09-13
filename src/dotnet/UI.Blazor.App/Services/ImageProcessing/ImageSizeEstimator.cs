namespace ActualChat.UI.Blazor.App.Services;

public static class ImageSizeEstimator
{
    private const double ResizeK = 2.427;
    private const double ResizeA = 0.816;
    private const double ResizeC = 0.393;
    private const double RecodeK = 0.1314;
    private const double RecodeC = -0.574;
    private const double PixelsOnlyK = 1.654;
    private const double PixelsOnlyA = 0.811;

    public static long Estimate(long sourceBytes, Size2D sourceSize, string contentType, ImageQualityBudget budget)
    {
        var sourcePixels = (double)sourceSize.Width * sourceSize.Height;
        if (sourcePixels <= 0)
            return sourceBytes;

        var target = FitWithinBudget(sourceSize, budget);
        var targetPixels = (double)target.Width * target.Height;
        if (!TryGetFormatMultiplier(contentType, out var multiplier))
            // A lossless source's byte size says more about its encoder than its content
            return (long)(PixelsOnlyK * Math.Pow(targetPixels, PixelsOnlyA));

        var bpp = sourceBytes / sourcePixels * multiplier;
        var estimate = targetPixels >= sourcePixels
            ? RecodeK * sourceBytes * multiplier * Math.Pow(bpp, RecodeC)
            : ResizeK * Math.Pow(targetPixels, ResizeA) * Math.Pow(bpp, ResizeC);
        return (long)Math.Min(estimate, sourceBytes * multiplier);
    }

    // Public so the mobile decline predicate can test the same target this estimates from
    public static Size2D FitWithinBudget(Size2D size, ImageQualityBudget budget)
    {
        // Mirrors fitWithinBudget in image-geometry.ts
        var sourcePixels = (double)size.Width * size.Height;
        if (sourcePixels <= 0)
            return size;

        var pixelScale = budget.MaxPixels is { } maxPixels
            ? Math.Sqrt(maxPixels / sourcePixels)
            : 1;
        var sideScale = budget.MaxLongSide is { } maxLongSide
            ? maxLongSide / (double)Math.Max(size.Width, size.Height)
            : 1;
        var scale = Math.Min(1, Math.Min(pixelScale, sideScale));
        return scale >= 1
            ? size
            : new Size2D(Math.Max(1, (int)(size.Width * scale)), Math.Max(1, (int)(size.Height * scale)));
    }

    public static string Format(long bytes)
    {
        var mb = bytes / 1_000_000.0;
        var rounded = mb switch {
            < 1 => Math.Max(0.1, Math.Round(mb, 1)),
            < 10 => Math.Round(mb * 2, MidpointRounding.AwayFromZero) / 2,
            _ => Math.Round(mb),
        };
        return $"~{rounded.ToString("0.#", null)} MB";
    }

    // Private methods

    private static bool TryGetFormatMultiplier(string contentType, out double multiplier)
    {
        multiplier = contentType switch {
            "image/jpeg" or "image/jpg" => 1,
            "image/webp" => 3.52,
            // Calibrated end to end against real iPhone HEICs vs the shipped jpegli output, not codec
            // efficiency (matched-VMAF gives 0.68 - Apple's encoder spends more bytes than JPEG here);
            // absorbs the recode branch's own bias for phone HEICs. See tmp/heic-multiplier/REPORT.md
            "image/heic" or "image/heif" => 2.0,
            "image/avif" => 5.27,
            _ => 0,
        };
        return multiplier > 0;
    }
}
