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

    // Streams beginning at/after catchUpFrom (default = none) are served from t=0, not the live edge
    [LegacyName("GetListeningStream_NewUnused", "2.15.9999")]
    Task<RpcStream<MuxedAudioStreamItem>> GetListeningStream(
        Session session,
        ChatId chatId,
        Moment catchUpFrom,
        CancellationToken cancellationToken);

    Task<RpcStream<MuxedAudioStreamItem>> GetReplayStream(
        Session session,
        ChatId chatId,
        Moment startAt,
        TimeSpan rewindOffset,
        double speed,
        CancellationToken cancellationToken);

    // The call completes only when its stream does, so the default 30s DelayTimeout is pure log noise
    [RpcMethod(
        RemoteExecutionMode = RpcRemoteExecutionMode.AwaitForConnection | RpcRemoteExecutionMode.AllowReconnect,
        DelayTimeout = double.PositiveInfinity)]
    Task PushStream(
        Session session,
        string chatId,
        string? repliedChatEntryId,
        double clientStartAt, // Unix epoch (seconds, double)
        int preSkip,
        RpcStream<AudioFrame> frameStream,
        CancellationToken cancellationToken);

    // The RPC method name carries the parameter count (":3" vs ":4"), so this overload is a
    // distinct wire method serving only already-published clients.
    [Obsolete("2026.08: Use the avSyncError overload. Old clients only.")]
    Task ReportAudioLatency(Session session, TimeSpan latency, CancellationToken cancellationToken);

    // Latency is the capture-to-ear lag of the sample being played, the audio twin of the video
    // lag report. avSyncError (= audio lag - video lag) is null when no fresh video lag exists for
    // the author; both its terms share one client ServerClock, so their difference carries no
    // clock error.
    Task ReportAudioLatency(
        Session session,
        TimeSpan latency,
        TimeSpan? avSyncError,
        CancellationToken cancellationToken);

    Task ReportPlayback(
        Session session, ChatId chatId, string streamId, ChatEntryId? entryId,
        CancellationToken cancellationToken);

    // Legacy methods

    [LegacyName(nameof(GetListeningStream), "2.15.9999")]
    [Obsolete("2026.08: Use GetListeningStream - it also takes catchUpFrom.")]
    Task<RpcStream<MuxedAudioStreamItem>> LegacyGetListeningStream(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken);

    [LegacyName("ChangeSettings", "2.9.9999")]
    Task LegacyChangeSettings(
        Session session,
        ChatId chatId,
        LegacyLiveStreamSettings settings,
        CancellationToken cancellationToken);
}
