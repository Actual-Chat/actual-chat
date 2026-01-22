using ActualChat.Media;
using MemoryPack;

namespace ActualChat.Video;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record VideoFormat : MediaFormat
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember]
    public override MediaType Type => MediaType.Video;

    [DataMember(Order = 10), MemoryPackOrder(10)] public string Codec { get; init; } = "avc1"; // H.264 by default
    [DataMember(Order = 11), MemoryPackOrder(11)] public int Width { get; init; }
    [DataMember(Order = 12), MemoryPackOrder(12)] public int Height { get; init; }
    [DataMember(Order = 13), MemoryPackOrder(13)] public string CodecSettings { get; init; } = "";

    public override byte[] Serialize(int index = 0)
        => CodecSettings.IsNullOrEmpty() ? [] : Convert.FromBase64String(CodecSettings);
}
