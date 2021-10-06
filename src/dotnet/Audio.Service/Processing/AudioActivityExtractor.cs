using ActualChat.Blobs;

namespace ActualChat.Audio.Processing;

public class AudioActivityExtractor
{
    public ChannelReader<AudioRecordSegment> GetSegmentsWithAudioActivity(
        AudioRecord audioRecord,
        ChannelReader<BlobPart> audioReader,
        CancellationToken cancellationToken)
    {
        var segments = Channel.CreateUnbounded<AudioRecordSegment>(
            new UnboundedChannelOptions {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = true,
            });
        _ = Task.Run(() => ExtractSegments(audioRecord, audioReader, segments.Writer, cancellationToken), default);
        return segments;
    }

    private async Task ExtractSegments(
        AudioRecord audioRecord,
        ChannelReader<BlobPart> content,
        ChannelWriter<AudioRecordSegment> target,
        CancellationToken cancellationToken)
    {
        Exception? error = null;
        var audioSourceProvider = new AudioSourceProvider();
        var segmentIndex = 0;
        try {
            var audioSource = await audioSourceProvider.ExtractMediaSource(content, cancellationToken).ConfigureAwait(false);
            var segment = new AudioRecordSegment(
                segmentIndex,
                audioRecord,
                audioSource,
                TimeSpan.Zero,
                audioSource.Duration);
            await target.WriteAsync(segment, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            error = e;
        }
        finally {
            target.Complete(error);
        }
    }
}
