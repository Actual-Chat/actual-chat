using ActualChat.Attributes;
using ActualChat.Hosting;
using ActualChat.Sharding;
using ActualChat.Video;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

[BackendService(nameof(HostRole.StreamingBackend), ServiceMode.Distributed)]
[BackendShardScheme(nameof(ShardScheme.StreamingBackend))]
public interface IVideoStreamingBackend : IComputeService, IBackendService
{
    Task<RpcStream<VideoFrame>?> GetVideoRaw(StreamId streamId, CancellationToken cancellationToken);
    Task PushVideo(VideoRecord record, RpcStream<VideoFrame> videoStream, CancellationToken cancellationToken);

    // Publisher-facing keyframe-request signal. The old quality-adaptation model
    // is replaced by ChangeRecordingQuality / ChangePlaybackQuality, but this
    // method survives so RequestKeyFrame can propagate immediately to the
    // recorder by invalidating GetQualityPreset and surfacing IsKeyFrameRequested.
    [ComputeMethod]
    Task<VideoQualityPreset> GetQualityPreset(StreamId streamId, CancellationToken cancellationToken);

    Task RequestKeyFrame(StreamId streamId, CancellationToken cancellationToken = default);
}
