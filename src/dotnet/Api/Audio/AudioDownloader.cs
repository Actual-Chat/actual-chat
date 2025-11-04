namespace ActualChat.Audio;

public abstract class AudioDownloader(IServiceProvider services)
{
    protected IServiceProvider Services { get; } = services;

    [field: AllowNull, MaybeNull]
    protected MomentClockSet Clocks => field ??= Services.Clocks();
    [field: AllowNull, MaybeNull]
    protected ILogger AudioSourceLog => field ??= Services.LogFor<AudioSource>();
    [field: AllowNull, MaybeNull]
    protected ILogger Log => field ??= Services.LogFor(GetType());

    public abstract Task<AudioSource> Download(string audioBlobUrl, TimeSpan skipTo, CancellationToken cancellationToken);
}
