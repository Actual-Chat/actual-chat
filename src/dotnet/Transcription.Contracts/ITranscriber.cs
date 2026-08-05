using ActualChat.Audio;

namespace ActualChat.Transcription;

/// <summary>
/// Transcribes an audio stream incrementally, writing progressively refined
/// <see cref="Transcript"/> snapshots to <c>output</c>.
/// </summary>
public interface ITranscriber
{
    TranscriberInfo Info { get; }
    Task Transcribe(
        string audioStreamId,
        AudioSource audioSource,
        TranscriptionOptions options,
        ChannelWriter<Transcript> output,
        CancellationToken cancellationToken = default);
}
