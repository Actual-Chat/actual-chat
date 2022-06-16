namespace ActualChat.Comparison;

public class LongAsStringKeyComparer : IComparer<string>
{
    public static IComparer<string> Default { get; } = new LongAsStringKeyComparer();

    public IComparer<string> BaseComparer { get; init; } = StringComparer.Ordinal;

    public int Compare(string? x, string? y)
        => long.TryParse(x, NumberStyles.Number, NumberFormatInfo.InvariantInfo, out var lx)
           && long.TryParse(y, NumberStyles.Number, NumberFormatInfo.InvariantInfo, out var ly)
            ? lx.CompareTo(ly)
            : BaseComparer.Compare(x, y);
}
