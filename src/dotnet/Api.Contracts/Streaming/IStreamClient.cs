using ActualChat.Audio;
using ActualChat.Transcription;

namespace ActualChat.Streaming;

/// <summary>
/// Client-side interface for accessing audio and transcript streams.
/// </summary>
public interface IStreamClient
{
    Task<AudioSource> GetAudio(string streamId, TimeSpan skipTo, CancellationToken cancellationToken);
    IAsyncEnumerable<TranscriptDiff> GetTranscript(string streamId, CancellationToken cancellationToken);
    Task ReportAudioLatency(TimeSpan latency, CancellationToken cancellationToken);
}
