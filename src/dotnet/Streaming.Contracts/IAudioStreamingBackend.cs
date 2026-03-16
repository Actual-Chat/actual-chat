using ActualChat.Attributes;
using ActualChat.Audio;
using ActualChat.Hosting;
using ActualChat.Transcription;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

/// <summary>
/// Backend service for audio and transcript streaming.
/// </summary>
[BackendService(nameof(HostRole.AudioBackend), ServiceMode.Distributed)]
[BackendShardScheme(nameof(HostRole.AudioBackend))]
public interface IAudioStreamingBackend : IRpcService, IBackendService
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
}
