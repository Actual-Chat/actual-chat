using ActualChat.Audio;

namespace ActualChat.Blobs;

public sealed class AudioSourceDownloader(IServiceProvider services)
{
    private IServiceProvider Services { get; } = services;
    private IBlobStorages Blobs => field ??= Services.GetRequiredService<IBlobStorages>();
    private MomentClockSet Clocks => field ??= Services.Clocks();
    private ILogger AudioSourceLog => field ??= Services.LogFor<AudioSource>();
    private ILogger Log => field ??= Services.LogFor(GetType());

    public async Task<AudioSource> Download(
        string blobId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
        => await TryDownload(blobId, skipTo, cancellationToken).ConfigureAwait(false)
            ?? new AudioSource(
                Clocks.SystemClock.Now,
                AudioSource.DefaultFormat,
                AsyncEnumerable.Empty<AudioFrame>(),
                TimeSpan.Zero,
                AudioSourceLog,
                cancellationToken);

    public async Task<AudioSource?> TryDownload(
        string blobId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        Log.LogDebug("Fetching blob #{BlobId}", blobId);
        var blobStorage = Blobs[BlobScope.AudioRecord];
        var stream = await blobStorage.Read(blobId, cancellationToken).ConfigureAwait(false);
        if (stream == null) {
            Log.LogWarning("Blob #{BlobId} is not found", blobId);
            return null;
        }

        var byteStream = stream.ReadByteStream(true, cancellationToken);
        var audio = await AudioSource.ReadFromByteStream(byteStream, Clocks, AudioSourceLog, cancellationToken)
            .ConfigureAwait(false);
        return audio.SkipTo(skipTo, cancellationToken);
    }
}
