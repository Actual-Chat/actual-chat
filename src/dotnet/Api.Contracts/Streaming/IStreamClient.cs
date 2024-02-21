using ActualChat.Audio;
using ActualChat.Transcription;

namespace ActualChat.Streaming;

public interface IStreamClient
{
    Task<AudioSource> GetAudio(Symbol streamId, TimeSpan skipTo, CancellationToken cancellationToken);
    IAsyncEnumerable<TranscriptDiff> GetTranscript(Symbol streamId, CancellationToken cancellationToken);
    Task ReportAudioLatency(TimeSpan latency, CancellationToken cancellationToken);
}
