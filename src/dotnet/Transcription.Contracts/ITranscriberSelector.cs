namespace ActualChat.Transcription;

// Returns transcribers that already encapsulate the fallback chain, so callers never
// walk it themselves - otherwise every consumer would reimplement try/detect/advance.

/// <summary>
/// Produces a transcriber for a stage that transparently falls over to the
/// next-ranked candidate when one fails.
/// </summary>
public interface ITranscriberSelector
{
    ITranscriber? GetStream(TranscriptionOptions options, TranscriberId preferredId);
    IOfflineTranscriber? GetOffline(
        TranscriptionOptions options,
        TranscriberId preferredId,
        TranscriberInfo? streamInfo = null);
}
