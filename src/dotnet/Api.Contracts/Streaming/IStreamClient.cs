using ActualChat.Audio;
using ActualChat.Transcription;
using ActualChat.Video;

namespace ActualChat.Streaming;

/// <summary>
/// Client-side interface for accessing audio and transcript streams.
/// </summary>
public interface IStreamClient
{
    Task<AudioSource> GetAudio(string streamId, TimeSpan skipTo, CancellationToken cancellationToken);
    Task<VideoSource> GetVideo(string streamId, TimeSpan skipTo, CancellationToken cancellationToken);
    IAsyncEnumerable<TranscriptDiff> GetTranscript(string streamId, CancellationToken cancellationToken);
    Task ReportAudioLatency(TimeSpan latency, CancellationToken cancellationToken);
    IAsyncEnumerable<VideoQualityPreset> ObserveStreamQualityRequests(string streamId, CancellationToken cancellationToken);
}
