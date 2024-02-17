namespace ActualChat.Audio.Ogg;

[StructLayout(LayoutKind.Sequential)]
public struct OpusHead
{
    public const ulong Signature = 0x4F_70_75_73_48_65_61_64; // OpusHead
    public byte Version; // =1
    public byte OutputChannelCount; // =1
    public ushort PreSkip; // Little Endian
    public uint InputSampleRate; // Little Endian
    public short OutputGain; // Little Endian
    public byte ChannelMapping;

    public static byte Size =>
        sizeof(ulong)
        + sizeof(byte)
        + sizeof(byte)
        + sizeof(ushort)
        + +sizeof(uint)
        + sizeof(short)
        + sizeof(byte);
}
