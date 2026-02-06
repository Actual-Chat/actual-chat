namespace ActualChat.Audio;

/// <summary>
/// Base class for downloading audio from a URL and converting to <see cref="AudioSource"/>.
/// </summary>
public abstract class AudioDownloader(IServiceProvider services)
{
    protected IServiceProvider Services { get; } = services;

    protected MomentClockSet Clocks => field ??= Services.Clocks();
    protected ILogger AudioSourceLog => field ??= Services.LogFor<AudioSource>();
    protected ILogger Log => field ??= Services.LogFor(GetType());

    public abstract Task<AudioSource> Download(string audioBlobUrl, TimeSpan skipTo, CancellationToken cancellationToken);
}
