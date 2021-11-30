using ActualChat.Blobs;

namespace ActualChat.Audio;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class AudioDownloader
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _log;

    public AudioDownloader(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _log = loggerFactory.CreateLogger(GetType());
        _httpClientFactory = httpClientFactory;
    }

    public virtual async Task<AudioSource> Download(
        Uri audioUri,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var blobStream = DownloadBlobStream(audioUri, cancellationToken);
        var audioLog = _loggerFactory.CreateLogger<AudioSource>();
        var audio = new AudioSource(blobStream, skipTo, audioLog, cancellationToken);
        await audio.WhenFormatAvailable.ConfigureAwait(false);
        return audio;
    }

    public IAsyncEnumerable<BlobPart> DownloadBlobStream(
        Uri audioUri,
        CancellationToken cancellationToken = default)
        => _httpClientFactory.DownloadBlobStream(audioUri, _log, cancellationToken);
}
