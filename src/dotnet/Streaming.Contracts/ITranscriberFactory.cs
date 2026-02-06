using ActualChat.Transcription;

namespace ActualChat.Streaming;

/// <summary>
/// Factory for creating <see cref="ITranscriber"/> instances.
/// </summary>
public interface ITranscriberFactory
{
    ITranscriber Get(TranscriptionEngine engine);
}
