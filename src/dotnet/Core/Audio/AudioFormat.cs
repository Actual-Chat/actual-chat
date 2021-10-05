using ActualChat.Media;

namespace ActualChat.Audio;

[DataContract]
public record AudioFormat : MediaFormat
{
    public override MediaType Type => MediaType.Audio;

    [DataMember(Order = 2)]
    public AudioCodecKind CodecKind { get; init; } = AudioCodecKind.Opus;
    [DataMember(Order = 3)]
    public string CodecSettings { get; init; } = "";

    public AudioFormat()
    {
        ChannelCount = 1;
        SampleRate = 48_000;
    }
}
