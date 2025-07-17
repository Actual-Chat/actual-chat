using ActualChat.Transcription;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

public interface IStreamServer : IRpcService
{
    Task<RpcStream<byte[]>?> GetAudio(string streamId, TimeSpan skipTo, CancellationToken cancellationToken);
    Task<RpcStream<TranscriptDiff>?> GetTranscript(string streamId, CancellationToken cancellationToken);
    Task<RpcStream<TranscriptDiff>?> GetTranslatedTranscript(
        TranslationId translationId,
        string streamId,
        CancellationToken cancellationToken);
    Task<RpcStream<StringDiff>?> GetTranslation(string streamId, CancellationToken cancellationToken);
    Task ReportAudioLatency(TimeSpan latency, CancellationToken cancellationToken);
}
