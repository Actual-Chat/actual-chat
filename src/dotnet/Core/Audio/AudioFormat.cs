using ActualChat.Blobs;
using ActualChat.Media;

namespace ActualChat.Audio;

[DataContract]
public record AudioFormat : MediaFormat
{
    public override MediaType Type => MediaType.Audio;

    [DataMember(Order = 1)]
    public AudioCodecKind CodecKind { get; init; } = AudioCodecKind.Opus;
    [DataMember(Order = 2)]
    public string CodecSettings { get; init; } = "";

    [DataMember(Order = 3)]
    public int SampleRate { get; init; } = 48_000;

    public override BlobPart ToBlobPart(int index = 0)
        => new(index, Convert.FromBase64String(CodecSettings));
}
