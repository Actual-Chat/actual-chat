using ActualChat.Audio;
using ActualChat.Live;
using ActualChat.Transcription;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

/// <summary>
/// RPC service for audio stream push/pull, transcripts, and the multiplexed
/// real-time / replay live-stream feed.
/// </summary>
public interface ILiveAudioStreams : IComputeService
{
    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<ApiArray<LiveAudioStreamInfo>> List(Session session, ChatId chatId, CancellationToken cancellationToken);

    Task<RpcStream<AudioFrame>?> GetStream(
        Session session,
        string streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken);

    Task<RpcStream<TranscriptDiff>?> GetTranscriptStream(
        Session session,
        string streamId,
        CancellationToken cancellationToken);

    Task<RpcStream<MuxedAudioStreamItem>> GetListeningStream(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken);

    Task<RpcStream<MuxedAudioStreamItem>> GetReplayStream(
        Session session,
        ChatId chatId,
        Moment startAt,
        TimeSpan rewindOffset,
        double speed,
        CancellationToken cancellationToken);

    [RpcMethod(RemoteExecutionMode = RpcRemoteExecutionMode.AwaitForConnection | RpcRemoteExecutionMode.AllowReconnect)]
    Task PushStream(
        Session session,
        string chatId,
        string? repliedChatEntryId,
        double clientStartAt, // Unix epoch (seconds, double)
        int preSkip,
        RpcStream<AudioFrame> frameStream,
        CancellationToken cancellationToken);

    Task ReportAudioLatency(Session session, TimeSpan latency, CancellationToken cancellationToken);

    Task ReportPlayback(
        Session session, ChatId chatId, string streamId, ChatEntryId? entryId,
        CancellationToken cancellationToken);

    // Legacy methods

    [LegacyName("ChangeSettings", "2.9.9999")]
    Task LegacyChangeSettings(
        Session session,
        ChatId chatId,
        LegacyLiveStreamSettings settings,
        CancellationToken cancellationToken);
}
