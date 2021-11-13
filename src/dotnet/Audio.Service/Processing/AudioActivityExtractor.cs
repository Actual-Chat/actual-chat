using ActualChat.Blobs;

namespace ActualChat.Audio.Processing;

public class AudioActivityExtractor
{
    private readonly ILoggerFactory _loggerFactory;

    public AudioActivityExtractor(ILoggerFactory loggerFactory)
        => _loggerFactory = loggerFactory;

    public async IAsyncEnumerable<OpenAudioSegment> SplitToAudioSegments(
        AudioRecord audioRecord,
        IAsyncEnumerable<BlobPart> blobParts,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // TODO(AY): Implement actual audio activity extractor
        var audioLog = _loggerFactory.CreateLogger<AudioSource>();
        var audio = new AudioSource(blobParts, TimeSpan.Zero, audioLog, cancellationToken);
        await audio.WhenFormatAvailable.ConfigureAwait(false);
        var openAudioSegment = new OpenAudioSegment(0, audioRecord, audio, TimeSpan.Zero, cancellationToken);
        _ = Task.Run(async () => {
            try {
                await audio.WhenDurationAvailable.ConfigureAwait(false);
                openAudioSegment.Close(audio.Duration);
            }
            catch (Exception error) {
                openAudioSegment.TryClose(error);
            }
        }, CancellationToken.None);
        yield return openAudioSegment;
    }
}
