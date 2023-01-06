using ActualChat.Media;

namespace ActualChat.Audio;

public class AudioSource : MediaSource<AudioFormat, AudioFrame>
{
    private static readonly byte[] OpusStreamFormat = { 0x41, 0x5F, 0x4F, 0x50, 0x55, 0x53, 0x5F, 0x53, 0x01 }; // A_OPUS_S + version = 1

    protected bool DebugMode => Constants.DebugMode.AudioSource;
    protected ILogger? DebugLog => DebugMode ? Log : null;
    private bool ShouldStripWebM { get; init; }

    public static AudioFormat DefaultFormat { get; } = new() {
        CodecSettings = Convert.ToBase64String(OpusStreamFormat),
    };

    public new Task<AudioFormat> FormatTask => base.FormatTask;
    public new ILogger Log => base.Log;

    public AudioSource(
        Task<AudioFormat> formatTask,
        IAsyncEnumerable<AudioFrame> frameStream,
        TimeSpan skipTo,
        ILogger log,
        CancellationToken cancellationToken)
        : base(formatTask,
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
        if (skipTo < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(skipTo));
        if (skipTo == TimeSpan.Zero)
            return this;

        if (ShouldStripWebM)
            throw new InvalidOperationException("SkipTo is not supported when WebM container has been stripped.");

        return new AudioSource(FormatTask, GetFrames(cancellationToken), skipTo, Log, cancellationToken);
    }
}
