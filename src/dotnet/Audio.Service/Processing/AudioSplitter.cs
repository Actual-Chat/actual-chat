namespace ActualChat.Audio.Processing;

public class AudioSplitter
{
    private IServiceProvider Services { get; }

    public AudioSplitter(IServiceProvider services)
        => Services = services;

#pragma warning disable CS1998
    public async IAsyncEnumerable<OpenAudioSegment> GetSegments(
#pragma warning restore CS1998
        AudioRecord audioRecord,
        IAsyncEnumerable<BlobPart> blobStream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // TODO(AY): Implement actual audio activity extractor
        var audioLog = Services.LogFor<AudioSource>();
        var audio = new AudioSource(blobStream, TimeSpan.Zero, audioLog, cancellationToken);
        var openAudioSegment = new OpenAudioSegment(0, audioRecord, audio, TimeSpan.Zero, cancellationToken);
        _ = Task.Run(async () => {
            try {
                await audio.WhenFormatAvailable.ConfigureAwait(false);
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
