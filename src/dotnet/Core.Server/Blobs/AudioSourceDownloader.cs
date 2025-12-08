using ActualChat.Audio;

namespace ActualChat.Blobs;

public class AudioSourceDownloader(IServiceProvider services)
{
    protected IServiceProvider Services { get; } = services;
    private IBlobStorages Blobs => field ??= Services.GetRequiredService<IBlobStorages>();
    protected MomentClockSet Clocks => field ??= Services.Clocks();
    protected ILogger AudioSourceLog => field ??= Services.LogFor<AudioSource>();
    protected ILogger Log => field ??= Services.LogFor(GetType());

    public async Task<AudioSource> Download(
        string blobId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        Log.LogDebug("Fetching blob #{BlobId}", blobId);
        var blobStorage = Blobs[BlobScope.AudioRecord];
        var stream = await blobStorage.Read(blobId, cancellationToken).ConfigureAwait(false);
        if (stream == null) {
            Log.LogWarning("Blob #{BlobId} is not found", blobId);
            var clocks = Services.Clocks();
            return new AudioSource(
                clocks.SystemClock.Now,
                AudioSource.DefaultFormat,
                AsyncEnumerable.Empty<AudioFrame>(),
                TimeSpan.Zero,
                Services.LogFor<AudioSource>(),
                cancellationToken);
        }
        var byteStream = stream.ReadByteStream(true, cancellationToken);
        var audio = await AudioSource.ReadFromByteStream(Clocks, AudioSourceLog, byteStream, cancellationToken).ConfigureAwait(false);
        var skipped = audio.SkipTo(skipTo, cancellationToken);
        return skipped;
    }
}
