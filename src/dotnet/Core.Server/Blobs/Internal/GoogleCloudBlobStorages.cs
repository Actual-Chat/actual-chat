using Microsoft.IO;

namespace ActualChat.Blobs.Internal;

public class GoogleCloudBlobStorages(string blobBucketName) : IBlobStorages
{
    private readonly RecyclableMemoryStreamManager _streamManager = new ();

    public IBlobStorage this[Symbol blobScope]
        => new GoogleCloudBlobStorage(blobBucketName, _streamManager);
}
