using Microsoft.IO;

namespace ActualChat.Blobs.Internal;

internal class GoogleCloudBlobStorageProvider : IBlobStorageProvider
{
    private readonly string _blobBucketName;
    private readonly RecyclableMemoryStreamManager _streamManager = new ();

    public GoogleCloudBlobStorageProvider(string blobBucketName)
        => _blobBucketName = blobBucketName;

    public IBlobStorage GetBlobStorage(Symbol blobScope)
        => new GoogleCloudBlobStorage(_blobBucketName, _streamManager);
}
