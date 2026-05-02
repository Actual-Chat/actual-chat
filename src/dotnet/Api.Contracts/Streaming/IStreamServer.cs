using ActualChat.Audio;
using ActualChat.Transcription;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

/// <summary>
/// Legacy v2.6 RPC surface for audio and transcript streaming. New clients
/// must use <see cref="ILiveAudioStreams"/> / <see cref="ILiveVideoStreams"/>;
/// this interface stays only to keep installed v2.6 apps working.
/// </summary>
[Obsolete("2026.05: Use ILiveAudioStreams / ILiveVideoStreams. Kept for v2.6 client compat only.")]
public interface IStreamServer : IRpcService
{
    Task<RpcStream<AudioFrame>?> GetAudio(string streamId, TimeSpan skipTo, CancellationToken cancellationToken);
    Task<RpcStream<TranscriptDiff>?> GetTranscript(string streamId, CancellationToken cancellationToken);
    Task ReportAudioLatency(TimeSpan latency, CancellationToken cancellationToken);

    // Streaming push calls: fire-and-forget from the caller's perspective.
    // - AwaitForConnection: wait for a connection if disconnected when the call is made.
    // - AllowReconnect: on same-peer reconnect, the $sys.Reconnect protocol tells the
    //   server "I still think this call is in flight"; the server, which still has the
    //   handler running, replies "I know it" and the client doesn't resend. The stream
    //   itself resumes via $sys.Ack with MustReset=true.
    // - No AllowResend: on reconnect to a different peer, the call + stream are failed;
    //   the caller is expected to start a fresh PushAudio with a new stream.
    [RpcMethod(RemoteExecutionMode = RpcRemoteExecutionMode.AwaitForConnection | RpcRemoteExecutionMode.AllowReconnect)]
    Task PushAudio(
        Session session,
        string chatId,
        string? repliedChatEntryId,
        double clientStartOffset,
        int preSkip,
        RpcStream<AudioFrame> frameStream,
        CancellationToken cancellationToken);
}
