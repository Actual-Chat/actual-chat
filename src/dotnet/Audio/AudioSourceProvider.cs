using ActualChat.Audio.WebM.Models;
using ActualChat.Channels;

namespace ActualChat.Audio;

public class AudioSourceProvider : MediaSourceProvider<AudioSource, AudioFormat, AudioFrame>
{
    protected override async ValueTask<AudioSource> CreateMediaSource(
        Task<AudioFormat> formatTask,
        Task<TimeSpan> durationTask,
        AsyncMemoizer<AudioFrame> frameMemoizer)
    {
        var format = await formatTask.ConfigureAwait(false);
        return new AudioSource(format, durationTask, frameMemoizer);
    }

    protected override AudioFormat CreateMediaFormat(EBML ebml, Segment segment, ReadOnlySpan<byte> rawHeader)
    {
        var trackEntry = segment.Tracks?.TrackEntries.Single(t => t.TrackType == TrackType.Audio)
                         ?? throw new InvalidOperationException("Stream doesn't contain Audio track.");
        var audio = trackEntry.Audio
                    ?? throw new InvalidOperationException("Track doesn't contain Audio entry.");

        return new AudioFormat {
            ChannelCount = (int) audio.Channels,
            CodecKind = trackEntry.CodecID switch {
                "A_OPUS" => AudioCodecKind.Opus,
                _ => throw new NotSupportedException($"Unsupported CodecID: {trackEntry.CodecID}.")
            },
            SampleRate = (int) audio.SamplingFrequency,
            CodecSettings = Convert.ToBase64String(rawHeader),
        };
    }
}
