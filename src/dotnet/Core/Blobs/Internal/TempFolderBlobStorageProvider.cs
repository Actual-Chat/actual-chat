using Stl.IO;
using Storage.NetCore;
using Storage.NetCore.Blobs;

namespace ActualChat.Blobs.Internal;

public class TempFolderBlobStorageProvider : IBlobStorageProvider
{
    public IBlobStorage GetBlobStorage(Symbol blobScope)
    {
        var blobFolderPath = FilePath.GetApplicationTempDirectory() & "blobs";
        return StorageFactory.Blobs.DirectoryFiles(blobFolderPath);
    }
}
