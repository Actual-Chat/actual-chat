using ActualChat.Audio;

namespace ActualChat.Transcription;

public interface ITranscriber
{
    IAsyncEnumerable<TranscriptUpdate> Transcribe(
        TranscriptionOptions options,
        IAsyncEnumerable<AudioStreamPart> audioStream,
        CancellationToken cancellationToken);
}
