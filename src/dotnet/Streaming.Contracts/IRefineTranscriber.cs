using ActualChat.Audio;
using ActualChat.Transcription;

namespace ActualChat.Streaming;

/// <summary>
/// Re-transcribes a fully captured audio segment to refine the realtime transcript.
/// </summary>
public interface IRefineTranscriber
{
    Task<Transcript?> Transcribe(
        AudioSource audioSource,
        TranscriptionOptions options,
        CancellationToken cancellationToken = default);
}
