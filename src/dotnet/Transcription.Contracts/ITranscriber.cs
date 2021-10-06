using ActualChat.Audio;

namespace ActualChat.Transcription;

public interface ITranscriber
{
    Task<ChannelReader<TranscriptUpdate>> Transcribe(
        TranscriptionRequest request,
        AudioSource audioSource,
        CancellationToken cancellationToken);
}
