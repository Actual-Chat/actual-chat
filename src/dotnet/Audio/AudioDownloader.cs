namespace ActualChat.Audio;

public class AudioDownloader
{
    protected static readonly byte[] ActualOpusStreamHeader = { 0x41, 0x5F, 0x4F, 0x50, 0x55, 0x53, 0x5F, 0x53 }; // A_OPUS_S
    protected static readonly byte[] WebMHeader = { 0x1A, 0x45, 0xDF, 0xA3 };
    protected IServiceProvider Services { get; }
    protected IHttpClientFactory HttpClientFactory { get; }
    protected ILogger Log { get; }

    public AudioDownloader(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        HttpClientFactory = services.GetRequiredService<IHttpClientFactory>();
    }

    public virtual async Task<AudioSource> Download(
        string audioBlobUrl,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var byteStream = HttpClientFactory.DownloadByteStream(audioBlobUrl.ToUri(), Log, cancellationToken);
        var audio = await ReadFromByteStream(byteStream, cancellationToken).ConfigureAwait(false);
        var skipped = audio.SkipTo(skipTo, cancellationToken);
        return skipped;
    }

    protected async Task<AudioSource> ReadFromByteStream(
        IAsyncEnumerable<byte[]> byteStream,
        CancellationToken cancellationToken)
    {
        var (head, tail) = await byteStream.ReadAtLeast(8, cancellationToken).ConfigureAwait(false);
        if (head.Length < 8)
            throw new InvalidOperationException("Downloaded audio stream is empty.");

        var clocks = Services.Clocks();
        var audioLog = Services.LogFor<AudioSource>();
        IAudioStreamConverter streamConverter;
        if (head.StartsWith(WebMHeader))
            streamConverter = new WebMStreamConverter(clocks, audioLog);
        else if (head.StartsWith(ActualOpusStreamHeader))
            streamConverter = new ActualOpusStreamConverter(clocks, audioLog);
        else
            throw new InvalidOperationException("Unsupported audio stream container.");

        var restoredByteStream = tail.Prepend(head, cancellationToken);
        return await streamConverter.FromByteStream(restoredByteStream, cancellationToken).ConfigureAwait(false);
    }
}
