using ActualChat.Audio;
using ActualChat.Transcription;

namespace ActualChat.Streaming;

public interface ITranscriber
{
    Task Transcribe(
        string audioStreamId,
        AudioSource audioSource,
        TranscriptionOptions options,
        ChannelWriter<Transcript> output,
        CancellationToken cancellationToken = default);
}
