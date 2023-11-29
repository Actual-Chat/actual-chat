namespace ActualChat.Audio.Ogg;

[StructLayout(LayoutKind.Sequential)]
public struct OggHeader
{
    public const uint CapturePattern = 0x4F_67_67_53;
    public byte StreamStructureVersion;
    public OggHeaderTypeFlag HeaderType;
    public ulong GranulePosition;
    public uint StreamSerialNumber;
    public int PageSequenceNumber;
    public uint PageChecksum;
    public byte PageSegmentCount;
    public byte[] SegmentTable; // Lacing values

    public int Size =>
        sizeof(uint)
        + sizeof(byte)
        + sizeof(OggHeaderTypeFlag)
        + sizeof(ulong)
        + sizeof(uint)
        + sizeof(int)
        + sizeof(uint)
        + sizeof(byte)
        + (SegmentTable.Length * sizeof(byte));
}
