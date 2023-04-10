using System.Text.RegularExpressions;

namespace ActualChat.Audio;

public partial class LocalAudioDownloader : AudioDownloader
{
    [GeneratedRegex("^.+\\/api\\/audio\\/download\\/(?<blobId>.+)$", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture)]
    private static partial Regex AudioBlobIdRegexFactory();
    
    private static readonly Regex AudioBlobIdRegex = AudioBlobIdRegexFactory();

    private IBlobStorageProvider Blobs { get; init; }

    public LocalAudioDownloader(IServiceProvider services) : base(services)
        => Blobs = services.GetRequiredService<IBlobStorageProvider>();

    public override async Task<AudioSource> Download(
        string audioBlobUrl,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var match = AudioBlobIdRegex.Match(audioBlobUrl);
        if (!match.Success)
            return await base.Download(audioBlobUrl, skipTo, cancellationToken).ConfigureAwait(false);

        var blobId = match.Groups["blobId"].Value;
        Log.LogDebug("Fetching blob #{BlobId}", blobId);
        var blobStorage = Blobs.GetBlobStorage(BlobScope.AudioRecord);
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
        var audio = await ReadFromByteStream(byteStream, cancellationToken).ConfigureAwait(false);
        var skipped = audio.SkipTo(skipTo, cancellationToken);
        return skipped;
    }
}
