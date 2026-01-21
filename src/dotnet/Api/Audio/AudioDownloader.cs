namespace ActualChat.Audio;

public abstract class AudioDownloader(IServiceProvider services)
{
    protected IServiceProvider Services { get; } = services;

    protected MomentClockSet Clocks => field ??= Services.Clocks();
    protected ILogger AudioSourceLog => field ??= Services.LogFor<AudioSource>();
    protected ILogger Log => field ??= Services.LogFor(GetType());

    public abstract Task<AudioSource> Download(string audioBlobUrl, TimeSpan skipTo, CancellationToken cancellationToken);
}
