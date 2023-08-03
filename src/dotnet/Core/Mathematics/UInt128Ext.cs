namespace ActualChat.Mathematics;

public static class UInt128Ext
{
    public static UInt128 SetBit(ref this UInt128 bits, int index)
        => bits |= (UInt128)1 << index;

    public static UInt128 ResetBit(ref this UInt128 bits, int index)
        => bits &= UInt128.MaxValue ^ ((UInt128)1 << index);

    public static bool IsBitSet(in this UInt128 bits, int index)
        => (index & 127) == index && (bits & ((UInt128)1 << index)) != 0;
}
