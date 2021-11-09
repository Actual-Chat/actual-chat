using System.Buffers;
using ActualChat.Blobs;

namespace ActualChat.Audio;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class AudioDownloader
{
    private readonly IHttpClientFactory _httpClientFactory;

    public AudioDownloader(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    public virtual async Task<AudioSource> DownloadAsAudioSource(
        Uri audioUri,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var blobParts = ReadBlobParts(audioUri, cancellationToken);
        var audioSourceProvider = new AudioSourceProvider();
        return await audioSourceProvider
            .CreateMediaSource(blobParts, skipTo, cancellationToken)
            .ConfigureAwait(false);
    }

    private async IAsyncEnumerable<BlobPart> ReadBlobParts(
        Uri audioUri,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var httpClient = _httpClientFactory.CreateClient();
        var response = await httpClient
            .GetAsync(audioUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var index = 0;
        await using var stream = await response.Content!
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var bufferLease = MemoryPool<byte>.Shared.Rent(4 * 1024);
        var buffer = bufferLease.Memory;

        var bytesRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        while (bytesRead != 0) {
            var blobPart = new BlobPart(index++, buffer[..bytesRead].ToArray());
            yield return blobPart;
            bytesRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
    }
}
