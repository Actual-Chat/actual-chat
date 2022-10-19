namespace ActualChat.Mathematics;

public static partial class MaybeTrimmedExt
{
    public static MaybeTrimmed<int> Sum(this IEnumerable<MaybeTrimmed<int>> values, int trimAt = int.MaxValue)
    {
        var sum = 0;
        var isTrimmed = false;
        foreach (var value in values) {
            sum += value.Value;
            isTrimmed |= value.IsTrimmed;
        }
        if (sum >= trimAt) {
            sum = trimAt;
            isTrimmed = true;
        }
        return (sum, isTrimmed);
    }
}
