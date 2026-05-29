namespace ActualChat.Media;

public sealed record VideoLayerDef(VideoSourceKind SourceKind, VideoSize Size, double BaseBitrateKbps)
{
    public static readonly VideoLayerDef[] CameraLayers = [
        new(VideoSourceKind.Camera, VideoSize.W320, 312.5),
        new(VideoSourceKind.Camera, VideoSize.W640, 1_250),
        new(VideoSourceKind.Camera, VideoSize.W1280, 4_000),
    ];
    public static readonly VideoLayerDef[] ScreenCastLayers = [
        new(VideoSourceKind.ScreenCast, VideoSize.W960, 4_375),
        new(VideoSourceKind.ScreenCast, VideoSize.W1920, 11_375),
    ];
    public static readonly VideoLayerDef[] All = CameraLayers.Concat(ScreenCastLayers).ToArray();
    public static readonly int MaxLayerCount = Math.Max(CameraLayers.Length, ScreenCastLayers.Length);

    public int Width => Size.LongSide();
    public int Height => Size.ShortSide();

    public double GetBitrateKbps(string codec)
        => GetBitrateKbps(VideoCodecKindExt.Parse(codec));

    public double GetBitrateKbps(VideoCodecKind codecKind)
        => GetBitrateKbps(BaseBitrateKbps, codecKind);

    public long GetByteRate(string codec)
        => KbpsToBytesPerSecond(GetBitrateKbps(codec));

    public long GetByteRate(VideoCodecKind codecKind)
        => KbpsToBytesPerSecond(GetBitrateKbps(codecKind));

    public static VideoLayerDef[] ListFor(VideoSourceKind sourceKind)
        => sourceKind switch {
            VideoSourceKind.Camera => CameraLayers,
            VideoSourceKind.ScreenCast => ScreenCastLayers,
            _ => throw StandardError.NotSupported(sourceKind.ToString(), "Unsupported video source kind."),
        };

    public static double GetBitrateKbps(double baseBitrateKbps, string codec)
        => GetBitrateKbps(baseBitrateKbps, VideoCodecKindExt.Parse(codec));

    public static double GetBitrateKbps(double baseBitrateKbps, VideoCodecKind codecKind)
    {
        var efficiency = Math.Max(double.Epsilon, VideoCodecDef.EfficiencyFor(codecKind));
        return Math.Max(double.Epsilon, baseBitrateKbps / efficiency);
    }

    public static long KbpsToBytesPerSecond(double bitrateKbps)
        => Math.Max(1, (long)Math.Ceiling(bitrateKbps * 1_000 / 8));
}
