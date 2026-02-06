namespace ActualChat.Spans;

/// <summary>
/// A union struct for reinterpreting numeric types without conversion.
/// </summary>
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
