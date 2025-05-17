using ActualChat.Audio;
using ActualChat.Transcription;

namespace ActualChat.Streaming;

public interface IStreamClient
{
    Task<AudioSource> GetAudio(string streamId, TimeSpan skipTo, CancellationToken cancellationToken);
    IAsyncEnumerable<TranscriptDiff> GetTranscript(string streamId, CancellationToken cancellationToken);
    IAsyncEnumerable<TranscriptDiff> GetTranslatedTranscript(
        string streamId,
        TranslationId translationId,
        CancellationToken cancellationToken);
    Task ReportAudioLatency(TimeSpan latency, CancellationToken cancellationToken);
}
