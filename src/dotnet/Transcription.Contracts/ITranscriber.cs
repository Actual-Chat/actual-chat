using ActualChat.Audio;

namespace ActualChat.Transcription;

public interface ITranscriber
{
    IAsyncEnumerable<Transcript> Transcribe(
        Symbol transcriberKey,
        TranscriptionOptions options,
        AudioSource audioSource,
        CancellationToken cancellationToken);
}
