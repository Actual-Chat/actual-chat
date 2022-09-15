namespace ActualChat.UI.Blazor.Components;

public class VirtualListDataQuery
{
    public static VirtualListDataQuery None { get; } = new (default);

    public Range<string> InclusiveRange { get; }
    public double ExpandStartBy { get; init; }
    public double ExpandEndBy { get; init; }

    public bool IsNone
        => ReferenceEquals(this, None);

    public VirtualListDataQuery(Range<string> inclusiveRange)
        => InclusiveRange = inclusiveRange;

    public override string ToString()
        => $"⁇(-{ExpandStartBy} | {InclusiveRange} | +{ExpandEndBy})";
}
