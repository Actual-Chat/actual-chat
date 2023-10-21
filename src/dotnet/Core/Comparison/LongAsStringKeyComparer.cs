namespace ActualChat.Comparison;

public sealed class LongAsStringKeyComparer : IComparer<string>
{
    public static readonly LongAsStringKeyComparer Default = new();

    public IComparer<string> BaseComparer { get; init; } = StringComparer.Ordinal;

    public int Compare(string? x, string? y)
        => long.TryParse(x, NumberStyles.Number, NumberFormatInfo.InvariantInfo, out var lx)
           && long.TryParse(y, NumberStyles.Number, NumberFormatInfo.InvariantInfo, out var ly)
            ? lx.CompareTo(ly)
            : BaseComparer.Compare(x, y);
}
