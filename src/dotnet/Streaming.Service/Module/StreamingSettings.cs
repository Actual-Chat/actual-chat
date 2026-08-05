namespace ActualChat.Streaming.Module;

public class StreamingSettings
{
    // The bar a message clears before a second transcription pass is worth its cost - either one
    // is enough. Configurable so tests, whose clips run a few seconds, can opt out of it.
    public int MinRetranscriptionWords { get; set; } = Constants.Transcription.MinRetranscriptionWords;
    public TimeSpan MinRetranscriptionDuration { get; set; } = Constants.Transcription.MinRetranscriptionDuration;
}
