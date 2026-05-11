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
            // Skip frames until we find a keyframe at or after the requested position
            .SkipWhile(vf => vf.Offset < skipTo || !vf.IsKeyFrame)
            .Select(vf => vf with { Offset = vf.Offset - skipTo, SerializedData = default }),
        log,
        cancellationToken)
{
    private static bool DebugMode => Constants.DebugMode.VideoSource;
    private ILogger? DebugLog => DebugMode ? Log : null;

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
