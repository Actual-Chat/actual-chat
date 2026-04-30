using ActualChat.Audio;
using ActualChat.Transcription;
using ActualChat.Video;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

/// <summary>
/// RPC service for streaming audio and transcripts from server.
/// </summary>
public interface IStreamServer : IRpcService
{
    Task<RpcStream<AudioFrame>?> GetAudio(string streamId, TimeSpan skipTo, CancellationToken cancellationToken);
    Task<RpcStream<VideoFrame>?> GetVideo(string streamId, TimeSpan skipTo, CancellationToken cancellationToken);
    Task<RpcStream<TranscriptDiff>?> GetTranscript(string streamId, CancellationToken cancellationToken);
    Task ReportAudioLatency(TimeSpan latency, CancellationToken cancellationToken);

    // Streaming push calls: fire-and-forget from the caller's perspective.
    // - AwaitForConnection: wait for a connection if disconnected when the call is made.
    // - AllowReconnect: on same-peer reconnect, the $sys.Reconnect protocol tells the
    //   server "I still think this call is in flight"; the server, which still has the
    //   handler running, replies "I know it" and the client doesn't resend. The stream
    //   itself resumes via $sys.Ack with MustReset=true.
    // - No AllowResend: on reconnect to a different peer, the call + stream are failed;
    //   the caller is expected to start a fresh PushAudio/PushVideo with a new stream.
    [RpcMethod(RemoteExecutionMode = RpcRemoteExecutionMode.AwaitForConnection | RpcRemoteExecutionMode.AllowReconnect)]
    Task PushAudio(
        Session session,
        string chatId,
        string? repliedChatEntryId,
        double clientStartOffset,
        int preSkip,
        RpcStream<AudioFrame> frameStream,
        CancellationToken cancellationToken);

    [RpcMethod(RemoteExecutionMode = RpcRemoteExecutionMode.AwaitForConnection | RpcRemoteExecutionMode.AllowReconnect)]
    Task PushVideo(
        Session session,
        string chatId,
        double clientStartOffset,
        VideoFormat format,
        RpcStream<VideoFrame> frameStream,
        StreamKind streamKind,
        CancellationToken cancellationToken);

    Task RequestKeyFrame(string streamId, CancellationToken cancellationToken);

    Task<VideoLatencyReportResponse> ReportVideoLatency(
        string streamId,
        VideoLatencyReport report,
        CancellationToken cancellationToken);
}
