using ActualChat.Transcription;

namespace ActualChat.Streaming.Services.Transcribers;

/// <summary>
/// Creates transcriber instances based on the configured transcription engine.
/// </summary>
public class TranscriberFactory(IServiceProvider services) : ITranscriberFactory
{
    public ITranscriber Get(TranscriptionEngine engine)
        => engine switch {
            TranscriptionEngine.Deepgram => services.GetRequiredService<DeepgramTranscriber>(),
            _ => services.GetRequiredService<GoogleTranscriber>(),
        };
}
