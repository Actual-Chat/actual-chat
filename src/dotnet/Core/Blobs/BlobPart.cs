using MessagePack;

namespace ActualChat.Blobs;

[MessagePackObject]
public record BlobPart(
    [property: Key(0)] int Index,
    [property: Key(2)] byte[] Data)
{
    public BlobPart() : this(0, Array.Empty<byte>()) { }

    public BlobPart(int index, byte[] prefix, byte[] data) : this()
    {
        var buffer = new byte[prefix.Length + data.Length];
        prefix.CopyTo(buffer, 0);
        data.CopyTo(buffer, prefix.Length);
        Index = index;
        Data = buffer;
    }

    public override string ToString()
    {
        var startBlock = BitConverter.ToString(Data.Take(8).ToArray());
        return $"{GetType().Name}(#{Index}, Data = 0x{startBlock}..., {Data.Length} byte(s))";
    }
}
