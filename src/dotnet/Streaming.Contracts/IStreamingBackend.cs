using ActualChat.Audio;
using ActualChat.Video;
using ActualChat.Transcription;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

/// <summary>
/// Backend service for audio and transcript streaming.
/// </summary>
public interface IStreamingBackend : IRpcService, IBackendService
{
    Task<RpcStream<byte[]>?> GetAudio(
        StreamId streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken);

    Task<RpcStream<TranscriptDiff>?> GetTranscript(
        StreamId streamId,
        CancellationToken cancellationToken);

    Task PushTranscript(
        StreamId streamId,
        RpcStream<TranscriptDiff> diffStream,
        CancellationToken cancellationToken);

    Task ProcessAudio(
        AudioRecord record,
        int preSkip,
        RpcStream<AudioFrame> frameStream,
        CancellationToken cancellationToken);

    Task<RpcStream<VideoFrame>?> GetVideo(
        StreamId streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken);

    Task PushVideo(
        VideoRecord record,
        RpcStream<VideoFrame> videoStream,
        CancellationToken cancellationToken);
}
