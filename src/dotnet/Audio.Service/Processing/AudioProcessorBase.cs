namespace ActualChat.Audio.Processing;

public abstract class AudioProcessorBase
{
    private ILogger? _log;
    private MomentClockSet? _clocks;

    protected ILogger Log => _log ??= Services.LogFor(GetType());
    protected ILogger? DebugLog => DebugMode ? Log : null;
    protected bool DebugMode { get; init; } = Constants.DebugMode.AudioProcessor;

    protected IServiceProvider Services { get; }
    protected MomentClockSet Clocks => _clocks ??= Services.Clocks();

    protected AudioProcessorBase(IServiceProvider services)
        => Services = services;
}
