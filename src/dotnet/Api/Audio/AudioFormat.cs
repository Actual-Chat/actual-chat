namespace ActualChat.Audio;

/// <summary>
/// Describes audio encoding parameters including codec, sample rate, and channel count.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record AudioFormat : MediaFormat
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember]
    public override MediaType Type => MediaType.Audio;

    [DataMember(Order = 10), Key(10)] public short ChannelCount { get; init; } = 1;
    [DataMember(Order = 11), Key(11)] public AudioCodecKind CodecKind { get; init; } = AudioCodecKind.Opus;
    [DataMember(Order = 12), Key(12)] public string CodecSettings { get; init; } = "";
    [DataMember(Order = 13), Key(13)] public int SampleRate { get; init; } = 48_000;
    [DataMember(Order = 14), Key(14)] public int PreSkip { get; init; }

    public override byte[] Serialize(int index = 0)
        => Convert.FromBase64String(CodecSettings);
}
