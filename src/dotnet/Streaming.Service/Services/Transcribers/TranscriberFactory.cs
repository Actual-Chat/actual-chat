using ActualChat.Streaming.Module;
using ActualChat.Transcription;

namespace ActualChat.Streaming.Services.Transcribers;

/// <summary>
/// Creates transcriber instances based on the configured transcription engine.
/// </summary>
public class TranscriberFactory(IServiceProvider services) : ITranscriberFactory
{
    private StreamingSettings Settings { get; } = services.GetRequiredService<StreamingSettings>();

    public ITranscriber Get(TranscriptionEngine engine)
    {
        if (Settings.UseFakeTranscriber)
            return services.GetRequiredService<FakeTranscriber>();

        return engine switch {
            TranscriptionEngine.Deepgram => services.GetRequiredService<DeepgramTranscriber>(),
            _ => services.GetRequiredService<GoogleTranscriber>(),
        };
    }
}
