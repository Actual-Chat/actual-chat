namespace ActualChat.Streaming.Services;

public abstract class AudioProcessorBase(IServiceProvider services)
{
    protected ILogger Log => field ??= Services.LogFor(GetType());
    protected ILogger? DebugLog => DebugMode ? Log : null;
    protected bool DebugMode { get; init; } = Constants.DebugMode.AudioProcessor;

    protected IServiceProvider Services { get; } = services;
    protected MomentClockSet Clocks => field ??= Services.Clocks();
}
