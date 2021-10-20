using System.Buffers;
using ActualChat.Blobs;

namespace ActualChat.Audio.Client;

public class AudioDownloader : IAudioDownloader, IDisposable
{
    private readonly HttpClient _httpClient;

    public AudioDownloader(IHttpClientFactory clientFactory)
        => _httpClient = clientFactory.CreateClient();

    public async Task<AudioSource> GetAudioSource(
        Uri audioUri,
        TimeSpan offset,
        CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync(audioUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var audioBlobs = Channel.CreateUnbounded<BlobPart>(
            new UnboundedChannelOptions {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = true,
            });
        var audioSourceProvider = new AudioSourceProvider();

        _ = Task.Run(ReadBlobPartsFromStream, cancellationToken);

        return await audioSourceProvider.ExtractMediaSource(audioBlobs, offset, cancellationToken)
            .ConfigureAwait(false);

        async Task ReadBlobPartsFromStream()
        {
            Exception? error = null;
            try {
                var index = 0;
                await using var stream = await response.Content
                    .ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);
                using var bufferLease = MemoryPool<byte>.Shared.Rent(4 * 1024);
                var buffer = bufferLease.Memory;

                var bytesRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                while (bytesRead != 0) {
                    await audioBlobs.Writer
                        .WriteAsync(new BlobPart(index++, buffer[..bytesRead].ToArray()), cancellationToken)
                        .ConfigureAwait(false);

                    bytesRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception e) {
                error = e;
            }
            finally {
                audioBlobs.Writer.Complete(error);
            }
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
