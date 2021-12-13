namespace ActualChat.Audio.Processing;

public class AudioSplitter
{
    private static readonly byte[] _pauseSignature = { 0,0,0,0 };
    private static readonly byte[] _resumeSignature = { 1,1,1,1 };

    private IServiceProvider Services { get; }

    public AudioSplitter(IServiceProvider services)
        => Services = services;

    public async IAsyncEnumerable<OpenAudioSegment> GetSegments(
        AudioRecord audioRecord,
        IAsyncEnumerable<BlobPart> blobStream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var openSegmentChannel =
            Channel.CreateUnbounded<OpenAudioSegment>(new UnboundedChannelOptions { SingleWriter = true });

        _ = Task.Run(() => WriteSegments(audioRecord, blobStream, openSegmentChannel, cancellationToken),
            cancellationToken);

        return openSegmentChannel.Reader.ReadAllAsync(cancellationToken);
    }

    private async Task WriteSegments(
        AudioRecord audioRecord,
        IAsyncEnumerable<BlobPart> blobStream,
        ChannelWriter<OpenAudioSegment> writer,
        CancellationToken cancellationToken)
    {
        Exception? error = null;
        var audioLog = Services.LogFor<AudioSource>();
        var index = 0;
        var channel = Channel.CreateUnbounded<BlobPart>(new UnboundedChannelOptions{ SingleWriter = true });
        try {
            await StartNewSegment(channel.Reader.ReadAllAsync(cancellationToken)).ConfigureAwait(false);

            await foreach (var blobPart in blobStream.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                if (blobPart.Data.SequenceEqual(_pauseSignature)) {
                    channel.Writer.Complete();
                    continue;
                }
                if (blobPart.Data.SequenceEqual(_resumeSignature)) {
                    channel = Channel.CreateUnbounded<BlobPart>(new UnboundedChannelOptions { SingleWriter = true });
                    await StartNewSegment(channel.Reader.ReadAllAsync(cancellationToken)).ConfigureAwait(false);
                    continue;
                }
                await channel.Writer.WriteAsync(blobPart, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e) {
            error = e;
        }
        finally {
            channel.Writer.TryComplete(error);
            writer.TryComplete(error);
        }

        async ValueTask StartNewSegment(IAsyncEnumerable<BlobPart> segmentBlobStream)
        {
            var audio = new AudioSource(segmentBlobStream, TimeSpan.Zero, audioLog, cancellationToken);
            var openAudioSegment = new OpenAudioSegment(index, audioRecord, audio, TimeSpan.Zero, cancellationToken);
            _ = Task.Run(async () => {
                try {
                    await audio.WhenFormatAvailable.ConfigureAwait(false);
                    await audio.WhenDurationAvailable.ConfigureAwait(false);
                    openAudioSegment.Close(audio.Duration);
                }
                catch (Exception e) {
                    openAudioSegment.TryClose(e);
                }
            }, CancellationToken.None);
            await writer.WriteAsync(openAudioSegment, cancellationToken).ConfigureAwait(false);
        }
    }
}
