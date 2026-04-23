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
        var category = GetCategory(codec);
        return (category, height) switch {
            ("hevc", >= 1080) => 3_250_000,
            ("hevc", >=  720) => 2_000_000,
            ("hevc", >=  540) => 1_250_000,
            ("hevc",      _)  =>   650_000,
            ("vp9",  >= 1080) => 2_750_000,
            ("vp9",  >=  720) => 1_600_000,
            ("vp9",  >=  540) => 1_050_000,
            ("vp9",       _)  =>   550_000,
            ("av1",  >= 1080) => 2_250_000,
            ("av1",  >=  720) => 1_400_000,
            ("av1",  >=  540) => 1_000_000,
            ("av1",       _)  =>   500_000,
            (_,      >= 1080) => 6_500_000, // H.264 + unknown default
            (_,      >=  720) => 4_000_000,
            (_,      >=  540) => 2_500_000,
            (_,           _)  => 1_250_000,
        };
    }

    private static string GetCategory(string codec)
        => codec.StartsWith("av01") ? "av1"
            : codec.StartsWith("hev1") || codec.StartsWith("hvc1") ? "hevc"
            : codec.StartsWith("vp09") ? "vp9"
            : "h264";
}
