namespace ActualChat.Audio;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class AudioDownloader
{
    private IServiceProvider Services { get; }
    private IHttpClientFactory HttpClientFactory { get; }
    private ILogger Log { get; }

    public AudioDownloader(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        HttpClientFactory = services.GetRequiredService<IHttpClientFactory>();
    }

    public virtual async Task<AudioSource> Download(
        Uri audioUri,
        AudioMetadata metadata,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var blobStream = HttpClientFactory.DownloadBlobStream(audioUri, Log, cancellationToken);
        var audioLog = Services.LogFor<AudioSource>();
        var audio = new AudioSource(blobStream, metadata, skipTo, audioLog, cancellationToken);
        await audio.WhenFormatAvailable.ConfigureAwait(false);
        return audio;
    }
}
