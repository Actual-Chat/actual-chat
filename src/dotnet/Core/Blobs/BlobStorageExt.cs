namespace ActualChat.Blobs;

public static class BlobStorageExt
{
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
}
