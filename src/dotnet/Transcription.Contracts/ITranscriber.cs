using ActualChat.Audio;

namespace ActualChat.Transcription;

public interface ITranscriber
{
    Task Transcribe(
        string audioStreamId,
        AudioSource audioSource,
        TranscriptionOptions options,
        ChannelWriter<Transcript> output,
        CancellationToken cancellationToken = default);
}
