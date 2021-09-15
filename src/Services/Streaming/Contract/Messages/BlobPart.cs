using MessagePack;

namespace ActualChat.Streaming
{
    [MessagePackObject]
    public record BlobPart(
        [property: Key(0)] int Index,
        [property: Key(2)] byte[] Chunk);
}
