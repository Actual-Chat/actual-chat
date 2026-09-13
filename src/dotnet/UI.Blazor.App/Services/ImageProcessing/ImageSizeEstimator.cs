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
            ? RecodeK * sourceBytes * Math.Pow(bpp, RecodeC)
            : ResizeK * Math.Pow(targetPixels, ResizeA) * Math.Pow(bpp, ResizeC);
        return (long)Math.Min(estimate, sourceBytes);
    }

    public static string Format(long bytes)
    {
        var mb = bytes / 1_000_000.0;
        var rounded = mb switch {
            < 1 => Math.Round(mb, 1),
            < 10 => Math.Round(mb * 2, MidpointRounding.AwayFromZero) / 2,
            _ => Math.Round(mb),
        };
        return $"~{rounded:0.#} MB";
    }

    // Private methods

    private static bool TryGetFormatMultiplier(string contentType, out double multiplier)
    {
        multiplier = contentType switch {
            "image/jpeg" or "image/jpg" => 1,
            "image/webp" => 3.52,
            "image/heic" or "image/heif" or "image/avif" => 5.27,
            _ => 0,
        };
        return multiplier > 0;
    }

    private static Size2D FitWithinBudget(Size2D size, ImageQualityBudget budget)
    {
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
}
