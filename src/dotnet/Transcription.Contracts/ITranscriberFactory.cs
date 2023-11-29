namespace ActualChat.Transcription;

public interface ITranscriberFactory
{
    ITranscriber Get(TranscriptionEngine engine);
}
