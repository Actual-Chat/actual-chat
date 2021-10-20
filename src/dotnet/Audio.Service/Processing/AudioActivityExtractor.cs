using ActualChat.Blobs;

namespace ActualChat.Audio.Processing;

public class AudioActivityExtractor
{
    public ChannelReader<OpenAudioSegment> SplitToAudioSegments(
        AudioRecord audioRecord,
        ChannelReader<BlobPart> audioReader,
        CancellationToken cancellationToken)
    {
        var openAudioSegments = Channel.CreateUnbounded<OpenAudioSegment>(
            new UnboundedChannelOptions {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = true,
            });
        _ = Task.Run(() => ExtractSegments(audioRecord, audioReader, openAudioSegments.Writer, cancellationToken),
            default);
        return openAudioSegments;
    }

    private async Task ExtractSegments(
        AudioRecord audioRecord,
        ChannelReader<BlobPart> content,
        ChannelWriter<OpenAudioSegment> target,
        CancellationToken cancellationToken)
    {
        Exception? error = null;
        var audioSourceProvider = new AudioSourceProvider();
        var segmentIndex = 0;
        try {
            var audioSource = await audioSourceProvider.ExtractMediaSource(content, default, cancellationToken)
                .ConfigureAwait(false);
            var openAudioSegment = new OpenAudioSegment(
                segmentIndex,
                audioRecord,
                audioSource,
                TimeSpan.Zero,
                audioSource.DurationTask);
            await target.WriteAsync(openAudioSegment, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            error = e;
        }
        finally {
            target.Complete(error);
        }
    }
}
