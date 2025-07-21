using ActualChat.Audio;
using ActualChat.Transcription;

namespace ActualChat.Streaming;

public interface IStreamClient
{
    Task<AudioSource> GetAudio(string streamId, TimeSpan skipTo, CancellationToken cancellationToken);
    IAsyncEnumerable<TranscriptDiff> GetTranscript(string streamId, CancellationToken cancellationToken);
    Task ReportAudioLatency(TimeSpan latency, CancellationToken cancellationToken);
}
