using ActualChat.Audio;

namespace ActualChat.Transcription;

public interface ITranscriber
{
    IAsyncEnumerable<Transcript> Transcribe(
        TranscriptionOptions options,
        IAsyncEnumerable<AudioStreamPart> audioStream,
        CancellationToken cancellationToken);
}
