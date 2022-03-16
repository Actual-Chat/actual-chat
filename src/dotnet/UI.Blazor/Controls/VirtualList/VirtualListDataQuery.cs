namespace ActualChat.UI.Blazor.Controls;

public record VirtualListDataQuery(Range<string> InclusiveRange)
{
    public double ExpandStartBy { get; init; }
    public double ExpandEndBy { get; init; }
    public long PixelExpandStartBy { get; init; }
    public long PixelExpandEndBy { get; init; }

    public override string ToString()
        => $"(+{ExpandStartBy}/{PixelExpandStartBy} | {InclusiveRange} | +{ExpandEndBy}/{PixelExpandEndBy})";
}
