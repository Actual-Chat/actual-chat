using System.Buffers;
using System.Net.Mime;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using Microsoft.IO;
using Stl.IO;

namespace ActualChat.Blobs;

public static class ByteStreamExt
{
    private const int DefaultBufferSize = 1024;
    private static readonly RecyclableMemoryStreamManager MemoryStreamManager = new ();

    // Download

    public static async IAsyncEnumerable<byte[]> DownloadByteStream(
        this IHttpClientFactory httpClientFactory,
        Uri blobUri,
        ILogger log,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        log.LogInformation("Downloading: {Uri}", blobUri.ToString());

        // WASM doesn't support PipeReader API directly from the HttpClient
        // NOTE(AY): Don't dispose anything but HttpClient! They hang on cancellation & block everything.
        using var httpClient = httpClientFactory.CreateClient();
        var httpClientDisposable = new SafeDisposable(httpClient, 10, log) { MustWait = false };
        await using var _ = httpClientDisposable.ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Get, blobUri);
        if (OSInfo.IsWebAssembly) {
            request.SetBrowserResponseStreamingEnabled(true);
            request.SetBrowserRequestMode(BrowserRequestMode.Cors);
            request.SetBrowserRequestCache(BrowserRequestCache.ForceCache);
        }
        var response = await httpClient
           .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
           .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        // NOTE(AY): We intentionally don't dispose stream here, coz it may hang on cancellation
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var byteStream = stream.ReadByteStream(false, 1024, cancellationToken);
        await foreach (var blobPart in byteStream.ConfigureAwait(false))
            yield return blobPart;

        log.LogInformation("Downloaded: {Uri}", blobUri.ToString());
    }

    // Upload

    public static async Task<long> UploadByteStream(
        this IBlobStorage target,
        string blobId,
        IAsyncEnumerable<byte[]> byteStream,
        CancellationToken cancellationToken)
    {
        var stream = MemoryStreamManager.GetStream();
        await using var _ = stream.ConfigureAwait(false);

        var bytesWritten = await stream.WriteByteStream(byteStream, false, cancellationToken).ConfigureAwait(false);
        stream.Position = 0;
        await target.Write(blobId, stream, MediaTypeNames.Application.Octet, cancellationToken).ConfigureAwait(false);
        return bytesWritten;
    }

    // Read

    public static async Task<(byte[] Head, IAsyncEnumerator<byte[]> Tail)> ReadAtLeast(
        this IAsyncEnumerable<byte[]> byteStream,
        int blockLength,
        CancellationToken cancellationToken)
    {
        using var blockBufferLease = MemoryPool<byte>.Shared.Rent(blockLength);
        var blockBuffer = blockBufferLease.Memory;
        var position = 0;
        IAsyncEnumerator<byte[]>? byteBlockEnumerator = null;
        try {
            byteBlockEnumerator = byteStream.GetAsyncEnumerator(cancellationToken);
            while (position < blockLength) {
                var hasNext = await byteBlockEnumerator.MoveNextAsync().ConfigureAwait(false);
                if (!hasNext) {
                    await byteBlockEnumerator.DisposeAsync().ConfigureAwait(false);
                    return (blockBuffer[..position].ToArray(), byteBlockEnumerator);
                }

                var byteBlock = byteBlockEnumerator.Current;
                if (position == 0 && byteBlock.Length >= blockLength)
                    return (byteBlock, byteBlockEnumerator);

                byteBlock.CopyTo(blockBuffer[position..]);
                position += byteBlock.Length;
            }
            return (blockBuffer[..position].ToArray(), byteBlockEnumerator);
        }
        catch {
            if (byteBlockEnumerator != null)
                await byteBlockEnumerator.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public static IAsyncEnumerable<byte[]> ReadByteStream(
        this FilePath sourceFilePath,
        CancellationToken cancellationToken = default)
        => sourceFilePath.ReadByteStream(DefaultBufferSize, cancellationToken);

    public static async IAsyncEnumerable<byte[]> ReadByteStream(
        this FilePath sourceFilePath,
        int bufferSize,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var inputStream = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read);
        await using var _ = inputStream.ConfigureAwait(false);
        var bytes = inputStream.ReadByteStream(true, bufferSize, cancellationToken);
        await foreach (var packet in bytes.ConfigureAwait(false))
            yield return packet;
    }

    public static IAsyncEnumerable<byte[]> ReadByteStream(
        this Stream source,
        bool mustDisposeSource,
        CancellationToken cancellationToken = default)
        => source.ReadByteStream(mustDisposeSource, DefaultBufferSize, cancellationToken);

    public static async IAsyncEnumerable<byte[]> ReadByteStream(
        this Stream source,
        bool mustDisposeSource,
        int bufferSize,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        try {
            // ArrayPool is used instead of MemoryPool here because
            // the low-level stream reading method in WASM is implemented
            // only for arrays:
            // - https://github.com/zwcloud/MonoWasm/blob/master/WasmHttpMessageHandler.cs#L349
            var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            var memory = buffer.AsMemory(0, bufferSize);
            try {
                var bytesRead = await source.ReadAsync(memory, cancellationToken).ConfigureAwait(false);
                while (bytesRead != 0) {
                    yield return buffer[..bytesRead];
                    bytesRead = await source.ReadAsync(memory, cancellationToken).ConfigureAwait(false);
                }
            }
            finally {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        finally {
            if (mustDisposeSource)
                await source.DisposeAsync().ConfigureAwait(false);
        }
    }

    // Write

    public static async Task<long> WriteByteStream(
        this Stream target,
        IAsyncEnumerable<byte[]> byteStream,
        bool mustDisposeTarget,
        CancellationToken cancellationToken = default)
    {
        try {
            var bytesWritten = 0L;
            await foreach (var blobPart in byteStream.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                await target.WriteAsync(blobPart, cancellationToken).ConfigureAwait(false);
                bytesWritten += blobPart.Length;
            }
            return bytesWritten;
        }
        finally {
            if (mustDisposeTarget)
                await target.DisposeAsync().ConfigureAwait(false);
        }
    }

    // Misc. helpers

    public static async IAsyncEnumerable<byte[]> SkipBytes(
        this IAsyncEnumerable<byte[]> byteStream,
        int byteCount,
        [EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        await foreach (var blobPart in byteStream.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            if (byteCount >= blobPart.Length) {
                byteCount -= blobPart.Length;
                continue;
            }
            if (byteCount == 0)
                yield return blobPart;
            else {
                yield return blobPart[byteCount..];
                 byteCount = 0;
            }
        }
    }
}
