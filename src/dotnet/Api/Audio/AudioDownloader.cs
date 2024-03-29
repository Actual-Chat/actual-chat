namespace ActualChat.Audio;

public abstract class AudioDownloader(IServiceProvider services)
{
    private MomentClockSet? _clocks;
    private ILogger<AudioSource>? _audioSourceLog;
    private ILogger? _log;

    protected static readonly byte[] ActualOpusStreamHeader = "A_OPUS_S"u8.ToArray();
    protected static readonly byte[] WebMHeader = [0x1A, 0x45, 0xDF, 0xA3];

    protected IServiceProvider Services { get; } = services;
    protected MomentClockSet Clocks => _clocks ??= Services.Clocks();
    protected ILogger AudioSourceLog => _audioSourceLog ??= Services.LogFor<AudioSource>();
    protected ILogger Log => _log ??= Services.LogFor(GetType());

    public abstract Task<AudioSource> Download(string audioBlobUrl, TimeSpan skipTo, CancellationToken cancellationToken);

    protected async Task<AudioSource> ReadFromByteStream(
        IAsyncEnumerable<byte[]> byteStream,
        CancellationToken cancellationToken)
    {
        var (head, tail) = await byteStream.ReadAtLeast(8, cancellationToken).ConfigureAwait(false);
        if (head.Length < 8)
            throw new InvalidOperationException("Downloaded audio stream is empty.");

        IAudioStreamConverter streamConverter;
        if (head.StartsWith(WebMHeader))
            streamConverter = new WebMStreamConverter(Clocks, AudioSourceLog);
        else if (head.StartsWith(ActualOpusStreamHeader))
            streamConverter = new ActualOpusStreamConverter(Clocks, AudioSourceLog);
        else
            throw new InvalidOperationException("Unsupported audio stream container.");

        var restoredByteStream = tail.Prepend(head, cancellationToken);
        return await streamConverter.FromByteStream(restoredByteStream, cancellationToken).ConfigureAwait(false);
    }
}
