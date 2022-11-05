namespace ActualChat.Mathematics;

public static class MaybeTrimmedExt
{
    public static MaybeTrimmed<int> Sum(this IEnumerable<MaybeTrimmed<int>> values, int trimAt = int.MaxValue)
    {
        var sum = 0;
        var isTrimmed = false;
        foreach (var value in values) {
            sum += value.Value;
            if (sum >= trimAt) {
                sum = trimAt;
                isTrimmed = true;
                break;
            }
            isTrimmed |= value.IsTrimmed;
        }
        return (sum, isTrimmed);
    }
}
