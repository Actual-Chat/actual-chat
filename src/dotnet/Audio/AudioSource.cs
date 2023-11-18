using ActualChat.Media;

namespace ActualChat.Audio;

public class AudioSource : MediaSource<AudioFormat, AudioFrame>
{
    private static readonly byte[]
        OpusStreamFormat = { 0x41, 0x5F, 0x4F, 0x50, 0x55, 0x53, 0x5F, 0x53, 0x03 }; // A_OPUS_S + version = 3

    protected static bool DebugMode => Constants.DebugMode.AudioSource;
    protected ILogger? DebugLog => DebugMode ? Log : null;

    public static readonly AudioFormat DefaultFormat = new () {
        CodecSettings = Convert.ToBase64String(OpusStreamFormat),
    };

    public new ILogger Log => base.Log;

    public AudioSource(
        Moment createdAt,
        AudioFormat format,
        IAsyncEnumerable<AudioFrame> frameStream,
        TimeSpan skipTo,
        ILogger log,
        CancellationToken cancellationToken)
        : base(
            createdAt,
            format,
            frameStream
                .SkipWhile(af => af.Offset < skipTo)
                .Select(af => new AudioFrame {
                    Data = af.Data,
                    Offset = af.Offset - skipTo,
                }),
            log,
            cancellationToken)
    { }

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
