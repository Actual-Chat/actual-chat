using System.Net;
using Google;
using Google.Cloud.Storage.V1;
using Microsoft.IO;

namespace ActualChat.Blobs.Internal;

public class GoogleCloudBlobStorage(string bucket, RecyclableMemoryStreamManager memoryStreamManager)
    : IBlobStorage
{
    private const int MinChunkSize = 128 * 1024;

    private readonly StorageClient _client = StorageClient.Create();

    private RecyclableMemoryStreamManager MemoryStreamManager { get; } = memoryStreamManager;

    public ValueTask DisposeAsync()
    {
        _client.DisposeSilently();
        return default;
    }

    public async Task<bool> Exists(string path, CancellationToken cancellationToken)
    {
        try {
            await _client.GetObjectAsync(bucket, path, cancellationToken: cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (GoogleApiException e) when (e.HttpStatusCode == HttpStatusCode.NotFound) {
            return false;
        }
    }

    public async Task<Stream?> Read(string path, CancellationToken cancellationToken)
    {
        var stream = MemoryStreamManager.GetStream();
        try {
            var options = new DownloadObjectOptions {
                ChunkSize = MinChunkSize,
            };
            await _client
                .DownloadObjectAsync(bucket, path, stream, options: options, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            stream.Position = 0L;
            return stream;
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound) {
            stream.DisposeSilently();
            return null;
        }
        catch {
            stream.DisposeSilently();
            throw;
        }
    }

    public async Task<string?> GetContentType(string path, CancellationToken cancellationToken)
    {
        try {
            var storageObject = await _client
                .GetObjectAsync(bucket, path, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return storageObject.ContentType;
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound) {
            return null;
        }
    }

    public Task Write(string path, Stream stream, string contentType, CancellationToken cancellationToken)
        => _client.UploadObjectAsync(bucket, path, contentType, stream, cancellationToken: cancellationToken);

    public Task Copy(string oldPath, string newPath, CancellationToken cancellationToken)
        => _client.CopyObjectAsync(bucket, oldPath, bucket, newPath, cancellationToken: cancellationToken);

    public Task Delete(string path, CancellationToken cancellationToken)
        => _client.DeleteObjectAsync(bucket, path, cancellationToken: cancellationToken);
}
