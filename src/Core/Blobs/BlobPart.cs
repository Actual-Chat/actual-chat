using System;
using MessagePack;

namespace ActualChat.Blobs
{
    [MessagePackObject]
    public record BlobPart(
        [property: Key(0)] int Index,
        [property: Key(2)] byte[] Data)
    {
        public BlobPart() : this(0, Array.Empty<byte>()) { }
    }
}
