using MemoryPack;

namespace ActualChat.Media;

[DataContract]
public abstract record MediaFormat
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public abstract MediaType Type { get; }

    public abstract byte[] Serialize(int index = 0);
}
