using Stl.IO;

namespace ActualChat.Blobs.Internal;

internal class TempFolderBlobStorageProvider : IBlobStorageProvider
{
    public IBlobStorage GetBlobStorage(Symbol blobScope)
    {
        var blobFolderPath = FilePath.GetApplicationTempDirectory() & "blobs";
        return new LocalFolderBlobStorage(blobFolderPath);
    }
}
