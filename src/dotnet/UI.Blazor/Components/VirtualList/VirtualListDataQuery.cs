namespace ActualChat.UI.Blazor.Components;

public class VirtualListDataQuery
{
    public static VirtualListDataQuery None { get; } = new (default);

    public Range<string> KeyRange { get; }
    public Range<double>? VirtualRange { get; init; }
    public double ExpandStartBy { get; init; } = 0;
    public double ExpandEndBy { get; init; } = 0;

    public bool IsNone
        => ReferenceEquals(this, None);

    public VirtualListDataQuery(Range<string> keyRange)
        => KeyRange = keyRange;

    public override string ToString()
        => $"⁇(-{ExpandStartBy} | {KeyRange} | +{ExpandEndBy})";
}
