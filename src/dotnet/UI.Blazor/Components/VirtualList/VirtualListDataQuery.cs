namespace ActualChat.UI.Blazor.Components;

public sealed class VirtualListDataQuery(Range<string> keyRange, Range<double> virtualRange, Range<int> moveRange)
{
    public static readonly VirtualListDataQuery None = new(default ,default, default);

    public Range<string> KeyRange { get; } = keyRange;
    public Range<double> VirtualRange { get; } = virtualRange;
    public Range<int> MoveRange { get; } = moveRange;

    public int? ExpectedCount { get; init; }

    public bool IsNone
        => ReferenceEquals(this, None);

    public override string ToString()
        => $"⁇({KeyRange} -> ~{MoveRange})";
}
