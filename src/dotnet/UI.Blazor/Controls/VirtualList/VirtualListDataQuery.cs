namespace ActualChat.UI.Blazor.Controls;

public class VirtualListDataQuery
{
    public static VirtualListDataQuery None { get; } = new(default);

    public Range<string> InclusiveRange { get; }
    public bool IsExpansionQuery { get; init; }
    public double ExpandStartBy { get; init; }
    public double ExpandEndBy { get; init; }
    public long PixelExpandStartBy { get; init; }
    public long PixelExpandEndBy { get; init; }

    public VirtualListDataQuery(Range<string> inclusiveRange)
        => InclusiveRange = inclusiveRange;

    public override string ToString()
        => IsExpansionQuery
            ? $"⁇(+{ExpandStartBy}/{PixelExpandStartBy} | {InclusiveRange} | +{ExpandEndBy}/{PixelExpandEndBy})"
            : $"⁇({InclusiveRange})"
    ;
}
