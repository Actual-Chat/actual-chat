namespace ActualChat.Video;

public sealed class VideoSource(
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
            // Skip frames until we find a keyframe at or after the requested position.
            // For video, we must start from a keyframe to decode correctly.
            .SkipWhile(vf => vf.Offset < skipTo || !vf.IsKeyFrame)
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
