using ActualChat.Transcription.DeepGram;
using ActualChat.Transcription.Google;

namespace ActualChat.Transcription.Module;

public class TranscriberFactory(IServiceProvider services) : ITranscriberFactory
{
    public ITranscriber Get(TranscriptionEngine engine)
        => engine switch {
            TranscriptionEngine.Deepgram => services.GetRequiredService<DeepGramTranscriber>(),
            _ => services.GetRequiredService<GoogleTranscriber>(),
        };
}
