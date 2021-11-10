using ActualChat.Audio;

namespace ActualChat.Transcription;

public interface ITranscriber
{
    IAsyncEnumerable<TranscriptUpdate> Transcribe(
        TranscriptionRequest request,
        IAsyncEnumerable<AudioStreamPart> audioStream,
        CancellationToken cancellationToken);
}
