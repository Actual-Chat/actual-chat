using ActualChat.Transcription;

namespace ActualChat.Audio.Processing;

public sealed class TranscriptSegment
{
    public static string GetStreamId(string audioStreamId, int index)
        => $"{audioStreamId}-{index.ToString("D", CultureInfo.InvariantCulture)}";

    public OpenAudioSegment AudioSegment { get; }
    public Transcript Prefix { get; }
    public Channel<Transcript> Suffixes { get; }
    public int Index { get; }
    public string StreamId { get; }

    public TranscriptSegment(OpenAudioSegment audioSegment, Transcript prefix, int index)
    {
        AudioSegment = audioSegment;
        Prefix = prefix;
        Suffixes = Channel.CreateUnbounded<Transcript>(new UnboundedChannelOptions() {
            SingleWriter = true,
            SingleReader = true,
            AllowSynchronousContinuations = true,
        });
        Index = index;
        StreamId = GetStreamId(audioSegment.StreamId, index);
    }

    public TranscriptSegment Next(Transcript prefix)
        => new(AudioSegment, prefix, Index + 1);
}
