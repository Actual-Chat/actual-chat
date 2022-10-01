using System.Text.RegularExpressions;

namespace ActualChat.Audio;

public class LocalAudioDownloader : AudioDownloader
{
    private static readonly Regex AudioBlobIdRegex = new("^.+\\/api\\/audio\\/download\\/(?<blobId>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture);

    private IBlobStorageProvider Blobs { get; init; }

    public LocalAudioDownloader(IServiceProvider services) : base(services)
        => Blobs = Services.GetRequiredService<IBlobStorageProvider>();

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
        var stream = await blobStorage.OpenReadAsync(blobId, cancellationToken).ConfigureAwait(false);
        var byteStream = stream.ReadByteStream(true, cancellationToken);

        var audio = await ReadFromByteStream(byteStream, cancellationToken).ConfigureAwait(false);
        var skipped = audio.SkipTo(skipTo, cancellationToken);
        await skipped.WhenFormatAvailable.ConfigureAwait(false);
        return skipped;
    }
}
