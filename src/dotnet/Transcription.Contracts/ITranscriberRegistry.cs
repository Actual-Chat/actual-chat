namespace ActualChat.Transcription;

/// <summary>
/// Resolves the ordered chain of transcribers to try for a given stage, applying
/// per-language ranking, capability filtering, and the admin override.
/// </summary>
public interface ITranscriberRegistry
{
    IReadOnlyList<ITranscriber> ListStream(TranscriptionOptions options, TranscriberId preferredId);
    IReadOnlyList<IOfflineTranscriber> ListOffline(TranscriptionOptions options, TranscriberId preferredId);
    ITranscriber? GetStream(TranscriberId id);
    IOfflineTranscriber? GetOffline(TranscriberId id);
    TranscriberInfo? GetInfo(TranscriberId id);
}
