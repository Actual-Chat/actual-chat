using System.Text.RegularExpressions;
using ActualChat.Audio;

namespace ActualChat.Streaming.Services;

public sealed partial class BlobStorageAudioDownloader(IServiceProvider services) : HttpClientAudioDownloader(services)
{
    [GeneratedRegex(@"^.+\/api\/audio\/download\/(?<blobId>.+)$", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture)]
    private static partial Regex AudioBlobIdRegexFactory();

    private static readonly Regex AudioBlobIdRegex = AudioBlobIdRegexFactory();

    private IBlobStorages Blobs { get; } = services.GetRequiredService<IBlobStorages>();

    public override async Task<AudioSource> Download(
        string audioBlobUrl,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var match = AudioBlobIdRegex.Match(audioBlobUrl);
        if (!match.Success) // Fallback to HttpClient-based download
            return await base.Download(audioBlobUrl, skipTo, cancellationToken).ConfigureAwait(false);

        var blobId = match.Groups["blobId"].Value;
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
        var audio = await ReadFromByteStream(byteStream, cancellationToken).ConfigureAwait(false);
        var skipped = audio.SkipTo(skipTo, cancellationToken);
        return skipped;
    }
}
