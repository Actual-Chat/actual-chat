namespace ActualChat.Comparison;

public sealed class LongAsStringKeyComparer : IComparer<string>
{
    public static readonly LongAsStringKeyComparer Default = new();

    public IComparer<string> BaseComparer { get; init; } = StringComparer.Ordinal;

    public int Compare(string? x, string? y)
        => NumberExt.TryParseLong(x, out var lx) && NumberExt.TryParseLong(y, out var ly)
            ? lx.CompareTo(ly)
            : BaseComparer.Compare(x, y);
}
