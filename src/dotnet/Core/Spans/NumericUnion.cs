namespace ActualChat.Spans;

#pragma warning disable CA1720

[StructLayout(LayoutKind.Explicit)]
public struct NumericUnion
{
    [FieldOffset(0)]
    public uint UInt;
    [FieldOffset(0)]
    public float Float;

    [FieldOffset(0)]
    public ulong ULong;
    [FieldOffset(0)]
    public double Double;
}
