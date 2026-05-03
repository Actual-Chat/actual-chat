using ActualChat.Streaming;

namespace ActualChat.Video;

public static class VideoBitrateTable
{
    // Screen content (IDE text, UI chrome) has much higher spatial entropy
    // than camera video. Empirically ~1.75x the camera budget keeps 10-12pt
    // text readable at the same resolution on a cross-continent link.
    private const double ScreenMultiplier = 1.75;

    public static int GetExpectedBitrate(string codec, int height, StreamKind kind = StreamKind.Webcam)
    {
        var baseBitrate = GetBaseBitrate(codec, height);
        return kind == StreamKind.Screencast
            ? (int)(baseBitrate * ScreenMultiplier)
            : baseBitrate;
    }

    private static int GetBaseBitrate(string codec, int height)
    {
        // 2160p tiers target screencast (4K monitor via getDisplayMedia).
        // Values roughly 2.5x 1080p base: 4K has 4x pixels, but screen content
        // is mostly static, so we don't need full 4x bandwidth.
        var category = GetCategory(codec);
        return (category, height) switch {
            ("hevc", >= 2160) => 6_500_000,
            ("hevc", >= 1080) => 3_250_000,
            ("hevc", >=  720) => 2_000_000,
            ("hevc", >=  540) => 1_250_000,
            ("hevc", >=  360) =>   650_000,
            ("hevc",      _)  =>   162_500,
            ("vp9",  >= 2160) => 5_500_000,
            ("vp9",  >= 1080) => 2_750_000,
            ("vp9",  >=  720) => 1_600_000,
            ("vp9",  >=  540) => 1_050_000,
            ("vp9",  >=  360) =>   550_000,
            ("vp9",       _)  =>   137_500,
            ("av1",  >= 2160) => 4_500_000,
            ("av1",  >= 1080) => 2_250_000,
            ("av1",  >=  720) => 1_400_000,
            ("av1",  >=  540) => 1_000_000,
            ("av1",  >=  360) =>   500_000,
            ("av1",       _)  =>   125_000,
            (_,      >= 2160) => 13_000_000, // H.264 + unknown default
            (_,      >= 1080) => 6_500_000,
            (_,      >=  720) => 4_000_000,
            (_,      >=  540) => 2_500_000,
            (_,      >=  360) => 1_250_000,
            (_,           _)  =>   312_500,
        };
    }

    private static string GetCategory(string codec)
        => codec.StartsWith("av01") ? "av1"
            : codec.StartsWith("hev1") || codec.StartsWith("hvc1") ? "hevc"
            : codec.StartsWith("vp09") ? "vp9"
            : "h264";
}
