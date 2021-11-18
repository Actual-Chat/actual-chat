using System.Buffers;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using Microsoft.IO;
using Stl.IO;
using Stl.OS;
using Storage.NetCore.Blobs;

namespace ActualChat.Blobs;

public static class BlobStreamExt
{
    private const int BufferSize = 4 * 1024;
    private static readonly RecyclableMemoryStreamManager MemoryStreamManager = new ();

    // Download

    public static async IAsyncEnumerable<BlobPart> DownloadBlobStream(
        this IHttpClientFactory httpClientFactory,
        Uri blobUri,
        [EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        using var httpClient = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, blobUri);
        if (OSInfo.IsWebAssembly) {
            request.SetBrowserResponseStreamingEnabled(true);
            request.SetBrowserRequestMode(BrowserRequestMode.Cors);
        }
        var response = await httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var blobStream = stream.ReadBlobStream(false, cancellationToken);
        await foreach (var blobPart in blobStream.ConfigureAwait(false))
            yield return blobPart;
    }

    // Upload

    public static async Task<long> UploadBlobStream(
        this IBlobStorage target,
        string blobId,
        IAsyncEnumerable<BlobPart> blobStream,
        CancellationToken cancellationToken)
    {
        var stream = MemoryStreamManager.GetStream();
        await using var _ = stream.ConfigureAwait(false);

        var bytesWritten = await stream.WriteBlobStream(blobStream, false, cancellationToken).ConfigureAwait(false);
        stream.Position = 0;
        await target.WriteAsync(blobId, stream, false, cancellationToken).ConfigureAwait(false);
        return bytesWritten;
    }

    // Read

    public static IAsyncEnumerable<BlobPart> ReadBlobStream(
        this FilePath sourceFilePath,
        CancellationToken cancellationToken = default)
    {
        var inputStream = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read);
        return inputStream.ReadBlobStream(true, cancellationToken);
    }

    public static async IAsyncEnumerable<BlobPart> ReadBlobStream(
        this Stream source,
        bool mustDisposeSource,
        [EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        try {
            using var bufferLease = MemoryPool<byte>.Shared.Rent(BufferSize);
            var buffer = bufferLease.Memory;

            var blobIndex = 0;
            var bytesRead = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            while (bytesRead != 0) {
                var blobPart = new BlobPart(blobIndex++, buffer[..bytesRead].ToArray());
                yield return blobPart;

                bytesRead = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
        }
        finally {
            if (mustDisposeSource)
                await source.DisposeAsync().ConfigureAwait(false);
        }
    }

    // Write

    public static async Task<long> WriteBlobStream(
        this Stream target,
        IAsyncEnumerable<BlobPart> blobStream,
        bool mustDisposeTarget,
        CancellationToken cancellationToken = default)
    {
        try {
            var bytesWritten = 0L;
            await foreach (var blobPart in blobStream.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                await target.WriteAsync(blobPart.Data, cancellationToken).ConfigureAwait(false);
                bytesWritten += blobPart.Data.Length;
            }
            return bytesWritten;
        }
        finally {
            if (mustDisposeTarget)
                await target.DisposeAsync().ConfigureAwait(false);
        }
    }
}
