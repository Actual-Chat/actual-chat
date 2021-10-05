using ActualChat.Audio.WebM.Models;

namespace ActualChat.Audio;

public class AudioSourceProvider : MediaSourceProvider<AudioSource, AudioFormat, AudioFrame>
{
    protected override async ValueTask<AudioSource> CreateMediaSource(Task<AudioFormat> formatTask, ChannelReader<AudioFrame> frameReader)
    {
        var format = await formatTask.ConfigureAwait(false);
        return new AudioSource(format, frameReader.ReadAllAsync());
    }

    protected override AudioFormat CreateMediaFormat(EBML ebml, Segment segment, ReadOnlySpan<byte> rawHeader)
    {
        var trackEntry = segment.Tracks?.TrackEntries.Single(t => t.TrackType == TrackType.Audio)
                         ?? throw new InvalidOperationException("Stream doesn't contain Audio track");
        var audio = trackEntry.Audio
                    ?? throw new InvalidOperationException("Track doesn't contain Audio entry");

        return new AudioFormat {
            ChannelCount = (int)audio.Channels,
            CodecKind = trackEntry.CodecID switch {
                "A_OPUS" => AudioCodecKind.Opus,
                _ => throw new ArgumentOutOfRangeException("CodecID", trackEntry.CodecID, "Unexpected CodecID")
            },
            SampleRate = (int)audio.SamplingFrequency,
            CodecSettings = Convert.ToBase64String(rawHeader),
        };
    }
}
