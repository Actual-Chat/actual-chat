namespace ActualChat;

public static class VideoCodecExt
{
    public static string? GetCategory(this string? codec)
    {
        if (codec.IsNullOrEmpty())
            return null;

        var value = codec.ToLowerInvariant();
        if (value is "h264" || value.StartsWith("avc1", StringComparison.Ordinal)
            || value.StartsWith("avc3", StringComparison.Ordinal))
            return "h264";

        if (value is "hevc" || value.StartsWith("hvc1", StringComparison.Ordinal)
            || value.StartsWith("hev1", StringComparison.Ordinal))
            return "hevc";

        if (value is "vp9" || value.StartsWith("vp09", StringComparison.Ordinal))
            return "vp9";

        if (value is "av1" || value.StartsWith("av01", StringComparison.Ordinal))
            return "av1";

        return null;
    }

    public static bool IsSupported(this string? codec, IEnumerable<string> supportedCodecs)
    {
        var category = GetCategory(codec);
        return category is not null && supportedCodecs.Any(x => GetCategory(x) == category);
    }
}
