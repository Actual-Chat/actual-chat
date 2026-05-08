namespace ActualChat.Media;

public sealed record VideoCodecDef(VideoCodecKind Kind, double Efficiency)
{
    public static readonly VideoCodecDef[] All = [
        new(VideoCodecKind.Unknown, 1),
        new(VideoCodecKind.H264, 1),
        new(VideoCodecKind.Hevc, 2),
        new(VideoCodecKind.Vp9, 2.35),
        new(VideoCodecKind.Av1, 2.85),
    ];

    public static double EfficiencyFor(VideoCodecKind kind)
        => All.FirstOrDefault(x => x.Kind == kind)?.Efficiency ?? 1;
}
