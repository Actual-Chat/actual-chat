using Storage.NetCore;
using Storage.NetCore.Blobs;

namespace ActualChat.Blobs.Internal;

public class GoogleCloudBlobStorageProvider : IBlobStorageProvider
{
    private readonly string _blobBucketName;

    public GoogleCloudBlobStorageProvider(string blobBucketName)
        => _blobBucketName = blobBucketName;

    public IBlobStorage GetBlobStorage(Symbol blobScope)
        => StorageFactory.Blobs.GoogleCloudStorageFromEnvironmentVariable(_blobBucketName);
}
