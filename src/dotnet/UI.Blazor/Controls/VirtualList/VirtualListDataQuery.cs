namespace ActualChat.UI.Blazor.Controls;

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

    public bool IsSimilarTo(VirtualListDataQuery other)
    {
        const int epsilon = 10;
        if (ReferenceEquals(this, other))
            return true;

        if (InclusiveRange != other.InclusiveRange)
            return false;

        return !(Math.Abs(ExpandStartBy - other.ExpandStartBy) > epsilon)
            && !(Math.Abs(ExpandEndBy - other.ExpandEndBy) > epsilon);
    }
}
