using ActualChat.Media;

namespace ActualChat.Audio;

public class AudioSource(
    Moment createdAt,
    AudioFormat format,
    IAsyncEnumerable<AudioFrame> frameStream,
    TimeSpan skipTo,
    ILogger log,
    CancellationToken cancellationToken
    ) : MediaSource<AudioFormat, AudioFrame>(
        createdAt,
        format,
        frameStream
            .SkipWhile(af => af.Offset < skipTo)
            .Select(af => new AudioFrame {
                Data = af.Data,
                Offset = af.Offset - skipTo,
                Duration = af.Duration,
            }),
        log,
        cancellationToken)
{
    private static readonly byte[] ActualOpusStreamHeader = "A_OPUS_S"u8.ToArray();
    private static readonly byte[] WebMHeader = [0x1A, 0x45, 0xDF, 0xA3];
    // A_OPUS_S + version = 3
    private static readonly byte[] OpusStreamFormat = [ 0x41, 0x5F, 0x4F, 0x50, 0x55, 0x53, 0x5F, 0x53, 0x03 ];

    protected static bool DebugMode => Constants.DebugMode.AudioSource;
    protected ILogger? DebugLog => DebugMode ? Log : null;

    public static readonly AudioFormat DefaultFormat = new () {
        CodecSettings = Convert.ToBase64String(OpusStreamFormat),
    };

    public static async Task<AudioSource> ReadFromByteStream(
        MomentClockSet clocks,
        ILogger audioSourceLog,
        IAsyncEnumerable<byte[]> byteStream,
        CancellationToken cancellationToken)
    {
        var (head, tail) = await byteStream.ReadAtLeast(8, cancellationToken).ConfigureAwait(false);
        if (head.Length < 8)
            throw new InvalidOperationException("Downloaded audio stream is empty.");

        IAudioStreamConverter streamConverter;
        if (head.StartsWith(WebMHeader))
            streamConverter = new WebMStreamConverter(clocks, audioSourceLog);
        else if (head.StartsWith(ActualOpusStreamHeader))
            streamConverter = new ActualOpusStreamConverter(clocks, audioSourceLog);
        else
            throw new InvalidOperationException("Unsupported audio stream container.");

        var restoredByteStream = tail.PrependOne(head, cancellationToken);
        return await streamConverter.FromByteStream(restoredByteStream, cancellationToken).ConfigureAwait(false);
    }

    public new ILogger Log => base.Log;

    public AudioSource SkipTo(TimeSpan skipTo, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(skipTo, TimeSpan.Zero);

        return skipTo == TimeSpan.Zero
            ? this
            : new AudioSource(CreatedAt,
                Format,
                GetFrames(cancellationToken),
                skipTo,
                Log,
                cancellationToken);
    }
}
