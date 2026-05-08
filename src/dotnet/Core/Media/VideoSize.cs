namespace ActualChat.Media;

/// <summary>
/// Canonical video size bucket expressed as the 16:9 horizontal dimension.
/// The value is intentionally the pixel width, so simple numeric comparisons
/// preserve the natural size order.
/// </summary>
public enum VideoSize
{
    None = 0,
    W320 = 320,
    W480 = 480,
    W640 = 640,
    W960 = 960,
    W1280 = 1280,
    W1920 = 1920,
    W3840 = 3840,
}

public static class VideoSizeExt
{
    private static readonly VideoSize[] OrderedSizes = [
        VideoSize.W320,
        VideoSize.W480,
        VideoSize.W640,
        VideoSize.W960,
        VideoSize.W1280,
        VideoSize.W1920,
        VideoSize.W3840,
    ];

    extension(VideoSize size)
    {
        public int LongSide() => (int)size;
        public int ShortSide() => RoundToEven(size.LongSide() * 9d / 16d);
    }

    public static VideoSize FromLongSide(double pixelCount)
    {
        if (pixelCount <= 0)
            return VideoSize.None;

        var result = OrderedSizes[0];
        foreach (var s in OrderedSizes) {
            if ((int)s > pixelCount)
                break;

            result = s;
        }
        return result;
    }

    public static VideoSize FromLongSide(double cssPixelCount, double devicePixelRatio)
    {
        cssPixelCount = Math.Max(0, cssPixelCount);
        if (cssPixelCount <= 0)
            return VideoSize.None;

        var cappedDpr = Math.Min(Math.Max(1, devicePixelRatio), 2);
        var target = Math.Max(cssPixelCount, cssPixelCount * cappedDpr * Math.Sqrt(2));
        return FromLongSide(target);
    }

    // Private methods

    private static int RoundToEven(double value)
        => Math.Max(2, (int)Math.Round(value / 2d) * 2);
}
