using ActualChat.Media;
using MemoryPack;

namespace ActualChat.Audio;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record AudioFormat : MediaFormat
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember]
    public override MediaType Type => MediaType.Audio;

    [DataMember(Order = 10), MemoryPackOrder(10)] public short ChannelCount { get; init; } = 1;
    [DataMember(Order = 11), MemoryPackOrder(11)] public AudioCodecKind CodecKind { get; init; } = AudioCodecKind.Opus;
    [DataMember(Order = 12), MemoryPackOrder(12)] public string CodecSettings { get; init; } = "";
    [DataMember(Order = 13), MemoryPackOrder(13)] public int SampleRate { get; init; } = 48_000;
    [DataMember(Order = 14), MemoryPackOrder(14)] public int PreSkipFrames { get; init; }

    public override byte[] Serialize(int index = 0)
        => Convert.FromBase64String(CodecSettings);
}
