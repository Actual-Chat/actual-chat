using System.Net;
using Google;
using Google.Apis.Storage.v1.Data;
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

    public async Task Append(string path, Stream stream, CancellationToken cancellationToken)
    {
        // Check if the file exists first
        var storageObject = await _client
            .GetObjectAsync(bucket, path, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // Upload the new chunk to a temporary part file
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var partPath = $"{path}.part.{timestamp}";
        var savedPart = false;

        try {
            await _client.UploadObjectAsync(bucket,
                    partPath,
                    storageObject.ContentType,
                    stream,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            savedPart = true;

            var composeRequest = new ComposeRequest {
                SourceObjects = [
                    new ComposeRequest.SourceObjectsData { Name = path },
                    new ComposeRequest.SourceObjectsData { Name = partPath },
                ],
                Destination = new Google.Apis.Storage.v1.Data.Object {
                    ContentType = storageObject.ContentType
                },
            };

            var composeRequestExec = _client.Service.Objects.Compose(composeRequest, bucket, path);
            await composeRequestExec.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        }
        finally {
            if (savedPart) {
                try {
                    await _client.DeleteObjectAsync(bucket, partPath, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }
                catch {
                    // Ignore cleanup errors
                }
            }
        }
    }
}
