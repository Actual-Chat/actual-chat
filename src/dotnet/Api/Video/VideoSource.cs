using ActualChat.Media;

namespace ActualChat.Video;

public class VideoSource(
    Moment createdAt,
    VideoFormat format,
    IAsyncEnumerable<VideoFrame> frameStream,
    TimeSpan skipTo,
    ILogger log,
    CancellationToken cancellationToken
    ) : MediaSource<VideoFormat, VideoFrame>(
        createdAt,
        format,
        frameStream
            .SkipWhile(vf => vf.Offset < skipTo)
            .Select(vf => new VideoFrame(vf.IsKeyFrame) {
                Data = vf.Data,
                Offset = vf.Offset - skipTo,
                Duration = vf.Duration,
                Width = vf.Width,
                Height = vf.Height,
                Description = vf.Description,
            }),
        log,
        cancellationToken)
{
    protected static bool DebugMode => Constants.DebugMode.VideoSource;
    protected ILogger? DebugLog => DebugMode ? Log : null;

    public static readonly VideoFormat DefaultFormat = new();

    public new ILogger Log => base.Log;

    public VideoSource SkipTo(TimeSpan skipTo, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(skipTo, TimeSpan.Zero);

        return skipTo == TimeSpan.Zero
            ? this
            : new VideoSource(CreatedAt,
                Format,
                GetFrames(cancellationToken),
                skipTo,
                Log,
                cancellationToken);
    }
}
