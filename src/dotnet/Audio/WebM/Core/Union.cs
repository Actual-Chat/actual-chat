namespace ActualChat.Audio.WebM;

[StructLayout(LayoutKind.Explicit)]
internal struct Union
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
