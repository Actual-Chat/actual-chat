using ActualChat.Audio;
using ActualChat.Live;
using ActualChat.Transcription;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

/// <summary>
/// RPC service for audio stream push/pull, transcripts, and the multiplexed
/// real-time / replay live-stream feed.
/// </summary>
[LegacyName("ILiveStreams", "2.6.9999")]
public interface ILiveAudioStreams : IComputeService
{
    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    [LegacyName("ListActiveStreams", "2.6.9999")]
    Task<ApiArray<LiveStreamInfo>> List(Session session, ChatId chatId, CancellationToken cancellationToken);

    Task<RpcStream<AudioFrame>?> GetStream(
        Session session,
        string streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken);

    Task<RpcStream<TranscriptDiff>?> GetTranscriptStream(
        Session session,
        string streamId,
        CancellationToken cancellationToken);

    // `clientStartOffset` is the legacy RPC name; the value is sourceStartOffsetSeconds on the server-synced clock.
    [RpcMethod(RemoteExecutionMode = RpcRemoteExecutionMode.AwaitForConnection | RpcRemoteExecutionMode.AllowReconnect)]
    Task PushStream(
        Session session,
        string chatId,
        string? repliedChatEntryId,
        double clientStartOffset,
        int preSkip,
        RpcStream<AudioFrame> frameStream,
        CancellationToken cancellationToken);

    Task ReportAudioLatency(Session session, TimeSpan latency, CancellationToken cancellationToken);

    [LegacyName("GetStream", "2.7.9999")]
    [LegacyName("GetLiveStream", "2.6.9999")]
    Task<RpcStream<LiveStreamItem>> LegacyGetStream(
        Session session,
        ChatId chatId,
        LiveStreamSettings settings,
        CancellationToken cancellationToken);

    Task ChangeSettings(
        Session session,
        ChatId chatId,
        LiveStreamSettings settings,
        CancellationToken cancellationToken);

    Task<RpcStream<LiveStreamItem>> GetReplayStream(
        Session session,
        ChatId chatId,
        Moment startAt,
        TimeSpan rewindOffset,
        double speed,
        CancellationToken cancellationToken);
}
