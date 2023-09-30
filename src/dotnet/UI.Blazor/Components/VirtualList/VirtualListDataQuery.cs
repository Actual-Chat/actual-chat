namespace ActualChat.UI.Blazor.Components;

public sealed class VirtualListDataQuery(Range<string> keyRange)
{
    public static readonly VirtualListDataQuery None = new(default);

    public Range<string> KeyRange { get; } = keyRange;
    public int ExpandStartBy { get; init; } = 0;
    public int ExpandEndBy { get; init; } = 0;

    public bool IsNone
        => ReferenceEquals(this, None);

    public override string ToString()
        => $"⁇(-{ExpandStartBy} | {KeyRange} | +{ExpandEndBy})";
}
