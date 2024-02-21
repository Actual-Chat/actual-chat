namespace ActualChat.Streaming.Services;

public abstract class AudioProcessorBase(IServiceProvider services)
{
    private ILogger? _log;
    private MomentClockSet? _clocks;

    protected ILogger Log => _log ??= Services.LogFor(GetType());
    protected ILogger? DebugLog => DebugMode ? Log : null;
    protected bool DebugMode { get; init; } = Constants.DebugMode.AudioProcessor;

    protected IServiceProvider Services { get; } = services;
    protected MomentClockSet Clocks => _clocks ??= Services.Clocks();
}
