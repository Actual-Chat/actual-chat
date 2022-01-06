namespace ActualChat.Audio;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class AudioDownloader
{
    protected IServiceProvider Services { get; init; }
    protected IHttpClientFactory HttpClientFactory { get; init; }
    protected ILogger Log { get; init; }

    public AudioDownloader(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        HttpClientFactory = services.GetRequiredService<IHttpClientFactory>();
    }

    public virtual async Task<AudioSource> Download(
        Uri audioUri,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var byteStream = HttpClientFactory.DownloadByteStream(audioUri, Log, cancellationToken);
        var audioLog = Services.LogFor<AudioSource>();
        var audio = new AudioSource(byteStream, skipTo, audioLog, cancellationToken);
        await audio.WhenFormatAvailable.ConfigureAwait(false);
        return audio;
    }
}
