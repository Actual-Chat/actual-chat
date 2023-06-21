using ActualChat.Media;
using MemoryPack;

namespace ActualChat.Audio;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record AudioFormat : MediaFormat
{
    public override MediaType Type => MediaType.Audio;

    [DataMember(Order = 1), MemoryPackOrder(1)]
    public short ChannelCount { get; init; } = 1;

    [DataMember(Order = 2), MemoryPackOrder(2)]
    public AudioCodecKind CodecKind { get; init; } = AudioCodecKind.Opus;
    [DataMember(Order = 3), MemoryPackOrder(3)]
    public string CodecSettings { get; init; } = "";

    [DataMember(Order = 4), MemoryPackOrder(4)]
    public int SampleRate { get; init; } = 48_000;

    [DataMember(Order = 5), MemoryPackOrder(5)]
    public int PreSkipFrames { get; init; }

    public override byte[] Serialize(int index = 0)
        => Convert.FromBase64String(CodecSettings);
}
