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
    IAsyncEnumerable<TranscriptDiff> GetTranscript(string streamId, CancellationToken cancellationToken);
    Task ReportAudioLatency(TimeSpan latency, CancellationToken cancellationToken);

    Task PushAudio(
        Session session,
        string chatId,
        string? repliedChatEntryId,
        double clientStartOffset,
        int preSkip,
        IAsyncEnumerable<AudioFrame> frameStream,
        CancellationToken cancellationToken);

    Task PushVideo(
        Session session,
        string chatId,
        double clientStartOffset,
        VideoFormat format,
        IAsyncEnumerable<VideoFrame> frameStream,
        StreamKind streamKind,
        CancellationToken cancellationToken);
}
