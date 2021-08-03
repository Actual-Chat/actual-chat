namespace ActualChat.Audio
{
    public record AudioFormat
    {
        public AudioCodec Codec { get; init; } = AudioCodec.Opus;
        public int ChannelCount { get; init; } = 1;
        public int SampleRate { get; init; } = 16_000;
    }
}
