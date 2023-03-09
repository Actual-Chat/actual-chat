using ActualChat.Audio;

namespace ActualChat.Transcription;

public interface ITranscriber
{
    IAsyncEnumerable<Transcript> Transcribe(
        string audioStreamId,
        AudioSource audioSource,
        TranscriptionOptions options,
        CancellationToken cancellationToken);
}
