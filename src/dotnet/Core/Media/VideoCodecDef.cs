namespace ActualChat.Media;

// Init-only properties (not a positional record): trimmed/AOT builds strip
// constructor parameter names, which breaks System.Text.Json's positional
// record handling with `ConstructorContainsNullParameterNames`. The browser
// init payload serializes this through AppConstants.Video.CodecDefs.
public sealed record VideoCodecDef
{
    public static readonly VideoCodecDef[] All = [
        new() { Kind = VideoCodecKind.Unknown, Efficiency = 1 },
        new() { Kind = VideoCodecKind.H264, Efficiency = 1 },
        new() { Kind = VideoCodecKind.Hevc, Efficiency = 1.4 },
        // Just above HEVC: VP9 is the better codec of the two, but only barely,
        // and the gap is nothing like AV1's.
        new() { Kind = VideoCodecKind.Vp9, Efficiency = 1.41 },
        new() { Kind = VideoCodecKind.Av1, Efficiency = 1.7 },
    ];

    public VideoCodecKind Kind { get; init; }
    public double Efficiency { get; init; } = 1;

    public static double EfficiencyFor(VideoCodecKind kind)
        => All.FirstOrDefault(x => x.Kind == kind)?.Efficiency ?? 1;
}
