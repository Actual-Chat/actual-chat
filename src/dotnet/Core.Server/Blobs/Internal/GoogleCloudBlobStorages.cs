namespace ActualChat.Blobs.Internal;

public class GoogleCloudBlobStorages(string blobBucketName) : IBlobStorages
{
    public string BucketName => blobBucketName;

    public IBlobStorage this[Symbol blobScope]
        => new GoogleCloudBlobStorage(blobBucketName);
}
