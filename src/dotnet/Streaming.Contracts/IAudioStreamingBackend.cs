using ActualChat.Attributes;
using ActualChat.Audio;
using ActualChat.Sharding;
using ActualChat.Transcription;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

/// <summary>
/// Backend service for audio and transcript streaming.
/// </summary>
[BackendService(nameof(HostRole.StreamingBackend), ServiceMode.Distributed)]
[BackendShardScheme(nameof(ShardScheme.StreamingBackend))]
public interface IAudioStreamingBackend : IComputeService, IBackendService
{
    // Language-suffixed transcript stream ids resolve to their base stream's chat.
    Task<ChatId?> GetChatId(StreamId streamId, CancellationToken cancellationToken);

    // The transcript so far, folded from the same memoized diff stream GetTranscript replays,
    // and invalidated when the next diff lands. Node-local by construction: the memoizer lives
    // on the node named by streamId.NodeRef, so a client must never serve this from its own cache -
    // the value is live and belongs to that node's memory.
    [ComputeMethod]
    [RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<Transcript?> GetMergedTranscript(StreamId streamId, CancellationToken cancellationToken);

    Task<RpcStream<AudioFrame>?> GetAudio(
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
