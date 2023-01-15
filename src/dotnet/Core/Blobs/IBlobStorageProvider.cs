namespace ActualChat.Blobs;

public interface IBlobStorageProvider
{
    IBlobStorage GetBlobStorage(Symbol blobScope);
}
