using ActualChat.Audio;
using ActualChat.Transcription;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

/// <summary>
/// RPC service for streaming audio and transcripts from server.
/// </summary>
public interface IStreamServer : IRpcService
{
    Task<RpcStream<byte[]>?> GetAudio(string streamId, TimeSpan skipTo, CancellationToken cancellationToken);
    Task<RpcStream<TranscriptDiff>?> GetTranscript(string streamId, CancellationToken cancellationToken);
    Task ReportAudioLatency(TimeSpan latency, CancellationToken cancellationToken);

    Task PushAudio(
        Session session,
        string chatId,
        string? repliedChatEntryId,
        double clientStartOffset,
        int preSkip,
        RpcStream<AudioFrame> frameStream,
        CancellationToken cancellationToken);
}
