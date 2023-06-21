using MemoryPack;

namespace ActualChat.Media;

[DataContract]
public abstract record MediaFormat
{
    [MemoryPackOrder(0)]
    public abstract MediaType Type { get; }

    public abstract byte[] Serialize(int index = 0);
}
