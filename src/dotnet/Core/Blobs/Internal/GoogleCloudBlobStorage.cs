using System.Net;
using Google;
using Google.Cloud.Storage.V1;
using Microsoft.IO;

namespace ActualChat.Blobs.Internal;

internal class GoogleCloudBlobStorage : IBlobStorage
{
    private readonly string _bucket;
    private readonly StorageClient _client;

    private RecyclableMemoryStreamManager MemoryStreamManager { get; }

    public GoogleCloudBlobStorage(string bucket, RecyclableMemoryStreamManager memoryStreamManager)
    {
        _bucket = bucket;
        _client = StorageClient.Create();
        MemoryStreamManager = memoryStreamManager;
    }

    public async Task<Stream?> Read(string path, CancellationToken cancellationToken)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));

        var stream = MemoryStreamManager.GetStream();
        try {
            await _client.DownloadObjectAsync(_bucket, path, stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound) {
            return null;
        }
        stream.Position = 0L;
        return stream;
    }

    public async Task<string?> GetContentType(string path, CancellationToken cancellationToken)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));

        try {
            var storageObject = await _client.GetObjectAsync(_bucket, path, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return storageObject.ContentType;
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound) {
            return null;
        }
    }

    public async Task Write(string path, Stream stream, string contentType, CancellationToken cancellationToken)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));
        if (stream == null) throw new ArgumentNullException(nameof(stream));

        await _client.UploadObjectAsync(_bucket, path, contentType, stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task Delete(IReadOnlyCollection<string> paths, CancellationToken cancellationToken)
    {
        if (paths == null) throw new ArgumentNullException(nameof(paths));
        if (paths.Count == 0)
            return;

        var resultTasks = new List<Task>(paths.Count);
        resultTasks.AddRange(paths.Select(path => _client.DeleteObjectAsync(_bucket, path, cancellationToken: cancellationToken)));
        await Task.WhenAll(resultTasks).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<bool>> Exists(
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken)
    {
        if (paths == null) throw new ArgumentNullException(nameof(paths));

        var resultTasks = new List<Task<bool>>(paths.Count);
        resultTasks.AddRange(paths.Select(path => ExistsInternal(path, cancellationToken)));
        return await Task.WhenAll(resultTasks).ConfigureAwait(false);

        async Task<bool> ExistsInternal(string path1, CancellationToken cancellationToken1)
        {
            try {
                await _client.GetObjectAsync(_bucket, path1, cancellationToken: cancellationToken1)
                    .ConfigureAwait(false);
                return true;
            }
            catch(GoogleApiException e) when(e.HttpStatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}
