using System.Text.RegularExpressions;

namespace ActualChat.Audio;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class LocalAudioDownloader : AudioDownloader
{
    public static Regex AudioBlobIdRegex = new("^.+\\/api\\/audio\\/download\\/(?<blobId>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture);

    protected IBlobStorageProvider Blobs { get; init; }

    public LocalAudioDownloader(IServiceProvider services) : base(services)
    {
        Blobs = Services.GetRequiredService<IBlobStorageProvider>();
    }

    public override async Task<AudioSource> Download(
        Uri audioUri,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var match = AudioBlobIdRegex.Match(audioUri.ToString());
        if (!match.Success)
            return await base.Download(audioUri, skipTo, cancellationToken).ConfigureAwait(false);

        var blobId = match.Groups["blobId"].Value;
        Log.LogInformation("Downloading blob: {BlobId}", blobId);
        var blobStorage = Blobs.GetBlobStorage(BlobScope.AudioRecord);
        var stream = await blobStorage.OpenReadAsync(blobId, cancellationToken).ConfigureAwait(false);
        var byteStream = stream.ReadByteStream(true, cancellationToken);
        var audioLog = Services.LogFor<AudioSource>();
        var audio = new AudioSource(byteStream, skipTo, audioLog, cancellationToken);
        await audio.WhenFormatAvailable.ConfigureAwait(false);
        return audio;
    }
}
