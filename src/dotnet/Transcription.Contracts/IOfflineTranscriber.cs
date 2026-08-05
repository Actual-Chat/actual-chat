using ActualChat.Audio;

namespace ActualChat.Transcription;

/// <summary>
/// Transcribes a fully captured audio segment in one shot — either to refine
/// a realtime transcript, or to re-transcribe stored audio later.
/// </summary>
public interface IOfflineTranscriber
{
    TranscriberInfo Info { get; }
    Task<Transcript?> Transcribe(
        AudioSource audioSource,
        TranscriptionOptions options,
        CancellationToken cancellationToken = default);
}
