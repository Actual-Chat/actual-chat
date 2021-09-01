using System.Runtime.Serialization;

namespace ActualChat.Audio
{
    [DataContract]
    public record AudioFormat
    {
        [DataMember(Order = 0)]
        public AudioCodec Codec { get; init; } = AudioCodec.Opus;
        [DataMember(Order = 1)]
        public int ChannelCount { get; init; } = 1;
        [DataMember(Order = 2)]
        public int SampleRate { get; init; } = 16_000;
    }
}
