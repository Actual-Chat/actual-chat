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

    Task PushVideo(
        Session session,
        string chatId,
        double clientStartOffset,
        VideoFormat format,
        // Previous StreamId from the same sender session (reconnect, codec switch, reconfigure).
        // When set, viewers receive VideoStreamInfo.ContinuationOf and can soft-rebind decoders.
        string? continuationOf,
        RpcStream<VideoFrame> frameStream,
        CancellationToken cancellationToken);
}
