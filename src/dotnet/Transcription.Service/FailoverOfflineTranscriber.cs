using ActualChat.Audio;

namespace ActualChat.Transcription;

/// <summary>
/// Runs an ordered chain of offline transcribers, advancing to the next one when a
/// candidate fails or returns nothing.
/// </summary>
public sealed class FailoverOfflineTranscriber(IReadOnlyList<IOfflineTranscriber> candidates, ILogger log)
    : IOfflineTranscriber
{
    public TranscriberInfo Info { get; } = candidates[0].Info;

    public async Task<Transcript?> Transcribe(
        AudioSource audioSource,
        TranscriptionOptions options,
        CancellationToken cancellationToken = default)
    {
        foreach (var candidate in candidates) {
            var transcript = (Transcript?)null;
            try {
                transcript = await candidate.Transcribe(audioSource, options, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                log.LogWarning(e, "Offline transcriber {TranscriberId} failed, failing over", candidate.Info.Id);
            }
            if (transcript != null)
                return transcript;
        }

        return null;
    }
}
