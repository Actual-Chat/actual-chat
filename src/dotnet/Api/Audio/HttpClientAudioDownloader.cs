namespace ActualChat.Audio;

public class HttpClientAudioDownloader(IServiceProvider services) : AudioDownloader(services)
{
    private IHttpClientFactory? _httpClientFactory;

    private IHttpClientFactory HttpClientFactory => _httpClientFactory ??= Services.HttpClientFactory();

    public override async Task<AudioSource> Download(
        string audioBlobUrl,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var byteStream = HttpClientFactory.DownloadByteStream(audioBlobUrl.ToUri(), Log, cancellationToken);
        var audio = await ReadFromByteStream(byteStream, cancellationToken).ConfigureAwait(false);
        var skipped = audio.SkipTo(skipTo, cancellationToken);
        return skipped;
    }
}
