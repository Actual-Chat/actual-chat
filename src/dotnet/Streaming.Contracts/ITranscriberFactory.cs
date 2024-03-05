using ActualChat.Transcription;

namespace ActualChat.Streaming;

public interface ITranscriberFactory
{
    ITranscriber Get(TranscriptionEngine engine);
}
