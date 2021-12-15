namespace ActualChat.Mathematics;

public static class IndexRangeExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Index LeftShiftBy(this Index index, int @by)
        => index.IsFromEnd ? ^(index.Value << @by) : index.Value << @by;

    public static Range LeftShiftBy(this Range range, int @by)
        => new (range.Start.LeftShiftBy(@by), range.End.LeftShiftBy(@by));
}
