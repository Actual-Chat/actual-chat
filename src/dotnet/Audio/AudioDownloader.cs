using ActualChat.Blobs;

namespace ActualChat.Audio;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class AudioDownloader
{
    private readonly IHttpClientFactory _httpClientFactory;

    public AudioDownloader(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    public virtual async Task<AudioSource> Download(
        Uri audioUri,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var blobStream = DownloadBlobStream(audioUri, cancellationToken);
        var audio = new AudioSource(blobStream, skipTo, cancellationToken);
        await audio.WhenFormatAvailable.ConfigureAwait(false);
        return audio;
    }

    public IAsyncEnumerable<BlobPart> DownloadBlobStream(
        Uri audioUri,
        CancellationToken cancellationToken = default)
        => _httpClientFactory.DownloadBlobStream(audioUri, cancellationToken);
}
