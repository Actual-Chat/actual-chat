using ActualChat.Audio;

namespace ActualChat.Transcription;

public interface ITranscriber
{
    IAsyncEnumerable<TranscriptDiff> Transcribe(
        string streamId,
        AudioSource audioSource,
        TranscriptionOptions options,
        CancellationToken cancellationToken);
}
