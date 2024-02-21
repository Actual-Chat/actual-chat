using ActualChat.Transcription;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

public interface IStreamServer : IRpcService
{
    Task<RpcStream<byte[]>?> GetAudio(Symbol streamId, TimeSpan skipTo, CancellationToken cancellationToken);
    Task<RpcStream<TranscriptDiff>?> GetTranscript(Symbol streamId, CancellationToken cancellationToken);
    Task ReportAudioLatency(TimeSpan latency, CancellationToken cancellationToken);
}
