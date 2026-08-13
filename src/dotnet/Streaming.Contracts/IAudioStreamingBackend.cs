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

    // Folded from the same memoized diff stream GetTranscript replays, and invalidated when the
    // next diff lands. NoCache because the value lives in the memory of one node - the one named
    // by streamId.NodeRef - so a client must never serve it from its own cache.
    [ComputeMethod]
    [RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<Transcript?> GetTranscriptSnapshot(StreamId streamId, CancellationToken cancellationToken);

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
