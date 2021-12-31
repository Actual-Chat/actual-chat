namespace ActualChat.Audio.Processing;

public abstract class TranscriptionProcessorBase : AudioProcessorBase
{
    protected TranscriptionProcessorBase(IServiceProvider services)
        : base(services)
        => DebugMode = Constants.DebugMode.TranscriptionProcessing;
}
