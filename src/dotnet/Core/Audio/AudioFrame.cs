using MessagePack;

namespace ActualChat.Audio;

[MessagePackObject]
public record AudioFrame(
    [property: Key(0)] int Index,
    [property: Key(1)] AudioFrameKind Kind,
    [property: Key(2)] byte[] Data,
    [property: Key(3)] double Offset,
    [property: Key(4)] int[]? BlobsStartAt);

public enum AudioFrameKind : byte
{
    Header = 0,
    ClusterAndBlobs = 1,
    Blobs = 2
}
