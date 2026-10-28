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

    async Task<(Transcript? Transcript, TranscriberId TranscriberId)> TranscribeWithId(
        AudioSource audioSource,
        TranscriptionOptions options,
        CancellationToken cancellationToken = default)
    {
        // Also returns which transcriber produced the result: a failover chain reports the one that answered
        var transcript = await Transcribe(audioSource, options, cancellationToken).ConfigureAwait(false);
        return (transcript, Info.Id);
    }
}
