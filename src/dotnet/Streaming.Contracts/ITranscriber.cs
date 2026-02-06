using ActualChat.Audio;
using ActualChat.Transcription;

namespace ActualChat.Streaming;

/// <summary>
/// Transcribes audio streams to text using a speech-to-text engine.
/// </summary>
public interface ITranscriber
{
    Task Transcribe(
        string audioStreamId,
        AudioSource audioSource,
        TranscriptionOptions options,
        ChannelWriter<Transcript> output,
        CancellationToken cancellationToken = default);
}
