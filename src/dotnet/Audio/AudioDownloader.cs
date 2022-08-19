namespace ActualChat.Audio;

public class AudioDownloader
{
    protected static readonly byte[] ActualOpusStreamHeader = { 0x41, 0x5F, 0x4F, 0x50, 0x55, 0x53, 0x5F, 0x53 }; // A_OPUS_S
    protected static readonly byte[] WebMHeader = { 0x1A, 0x45, 0xDF, 0xA3 };
    protected IServiceProvider Services { get; init; }
    protected IHttpClientFactory HttpClientFactory { get; init; }
    protected ILogger Log { get; init; }

    public AudioDownloader(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        HttpClientFactory = services.GetRequiredService<IHttpClientFactory>();
    }

    public virtual async Task<AudioSource> Download(
        Uri audioUri,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var byteStream = HttpClientFactory.DownloadByteStream(audioUri, Log, cancellationToken);
        var audio = await ReadFromByteStream(byteStream, cancellationToken).ConfigureAwait(false);
        var skipped = audio.SkipTo(skipTo, cancellationToken);
        await skipped.WhenFormatAvailable.ConfigureAwait(false);
        return skipped;
    }

    protected async Task<AudioSource> ReadFromByteStream(
        IAsyncEnumerable<byte[]> byteStream,
        CancellationToken cancellationToken)
    {
        var (head, tail) = await byteStream.ReadAtLeast(8, cancellationToken).ConfigureAwait(false);
        if (head.Length < 8)
            throw new InvalidOperationException("Downloaded audio stream is empty.");

        var audioLog = Services.LogFor<AudioSource>();
        IAudioStreamAdapter streamAdapter;
        if (head.StartsWith(WebMHeader))
            streamAdapter = new WebMStreamAdapter(audioLog);
        else if (head.StartsWith(ActualOpusStreamHeader))
            streamAdapter = new ActualOpusStreamAdapter(audioLog);
        else
            throw new InvalidOperationException("Unsupported audio stream container.");

        var restoredByteStream = tail.Prepend(head, cancellationToken);
        return await streamAdapter.Read(restoredByteStream, cancellationToken).ConfigureAwait(false);
    }
}
