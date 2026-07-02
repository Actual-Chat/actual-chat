namespace ActualChat.Media;

public static class VideoCodecKindExt
{
    public static VideoCodecKind Parse(string codec)
    {
        if (codec.IsNullOrWhiteSpace())
            return VideoCodecKind.Unknown;

        var c = codec.Trim().ToLowerInvariant();
        if (c.StartsWith("avc1", StringComparison.Ordinal) || c.StartsWith("h264", StringComparison.Ordinal))
            return VideoCodecKind.H264;
        if (c.StartsWith("hev1", StringComparison.Ordinal)
            || c.StartsWith("hvc1", StringComparison.Ordinal)
            || c.StartsWith("hevc", StringComparison.Ordinal)
            || c.StartsWith("h265", StringComparison.Ordinal))
            return VideoCodecKind.Hevc;
        if (c.StartsWith("vp09", StringComparison.Ordinal) || c.StartsWith("vp9", StringComparison.Ordinal))
            return VideoCodecKind.Vp9;
        if (c.StartsWith("av01", StringComparison.Ordinal) || c.StartsWith("av1", StringComparison.Ordinal))
            return VideoCodecKind.Av1;
        return VideoCodecKind.Unknown;
    }
}
