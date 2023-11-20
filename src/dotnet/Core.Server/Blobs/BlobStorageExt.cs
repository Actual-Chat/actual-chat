using System.Net.Mime;

namespace ActualChat.Blobs;

public static class BlobStorageExt
{
    public static async Task<long> UploadByteStream(
        this IBlobStorage target,
        string blobId,
        IAsyncEnumerable<byte[]> byteStream,
        CancellationToken cancellationToken)
    {
        var stream = MemoryStreamManager.Default.GetStream();
        await using var _ = stream.ConfigureAwait(false);

        var bytesWritten = await stream.WriteByteStream(byteStream, false, cancellationToken).ConfigureAwait(false);
        stream.Position = 0;
        await target.Write(blobId, stream, MediaTypeNames.Application.Octet, cancellationToken).ConfigureAwait(false);
        return bytesWritten;
    }

    public static async Task<bool> CopyIfExists(this IBlobStorage storage,
        string oldPath,
        string newPath,
        CancellationToken cancellationToken)
    {
        if (!await storage.Exists(oldPath, cancellationToken).ConfigureAwait(false))
            return false;

        await storage.Copy(oldPath, newPath, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public static async Task<bool> DeleteIfExists(this IBlobStorage storage,
        string path,
        CancellationToken cancellationToken)
    {
        if (!await storage.Exists(path, cancellationToken).ConfigureAwait(false))
            return false;

        await storage.Delete(path, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
