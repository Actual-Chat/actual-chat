using System.Text.RegularExpressions;
using ActualChat.Audio;

namespace ActualChat.Streaming.Services;

public sealed partial class BlobStorageAudioDownloader(IServiceProvider services) : HttpClientAudioDownloader(services)
{
    [GeneratedRegex(@"^.+\/api\/audio\/download\/(?<blobId>.+)$", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture)]
    private static partial Regex AudioBlobIdRegexFactory();

    private static readonly Regex AudioBlobIdRegex = AudioBlobIdRegexFactory();

    private AudioSourceDownloader AudioSourceDownloader { get; } = services.GetRequiredService<AudioSourceDownloader>();

    public override async Task<AudioSource> Download(
        string audioBlobUrl,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var match = AudioBlobIdRegex.Match(audioBlobUrl);
        if (!match.Success) // Fallback to HttpClient-based download
            return await base.Download(audioBlobUrl, skipTo, cancellationToken).ConfigureAwait(false);

        var blobId = match.Groups["blobId"].Value;
        return await AudioSourceDownloader.Download(blobId, skipTo, cancellationToken).ConfigureAwait(false);
    }
}
