using Storage.NetCore.Blobs;

namespace ActualChat.Blobs;

public interface IBlobStorageProvider
{
    IBlobStorage GetBlobStorage(Symbol blobScope);
}
