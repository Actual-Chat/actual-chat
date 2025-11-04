namespace ActualChat.Audio;

public class HttpClientAudioDownloader(IServiceProvider services) : AudioDownloader(services)
{
    [field: AllowNull, MaybeNull]
    private IHttpClientFactory HttpClientFactory => field ??= Services.HttpClientFactory();

    public override async Task<AudioSource> Download(
        string audioBlobUrl,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var byteStream = HttpClientFactory.DownloadByteStream(audioBlobUrl.ToUri(), Log, cancellationToken);
        var audio = await AudioSource.ReadFromByteStream(Clocks, AudioSourceLog, byteStream, cancellationToken).ConfigureAwait(false);
        var skipped = audio.SkipTo(skipTo, cancellationToken);
        return skipped;
    }
}
