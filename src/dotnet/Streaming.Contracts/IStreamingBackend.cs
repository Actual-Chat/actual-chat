using ActualChat.Audio;
using ActualChat.Transcription;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

public interface IStreamingBackend : IRpcService, IBackendService
{
    Task<RpcStream<byte[]>?> GetAudio(
        StreamId streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken);

    Task<RpcStream<TranscriptDiff>?> GetTranscript(
        StreamId streamId,
        CancellationToken cancellationToken);

    Task ProcessAudio(
        AudioRecord record,
        int preSkipFrames,
        RpcStream<AudioFrame> frameStream,
        CancellationToken cancellationToken);
}
