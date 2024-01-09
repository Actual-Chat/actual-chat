using ActualLab.IO;

namespace ActualChat.Blobs.Internal;

public class TempFolderBlobStorageProvider(IServiceProvider services) : IBlobStorageProvider
{
    private IServiceProvider Services { get; } = services;

    public IBlobStorage GetBlobStorage(Symbol blobScope)
    {
        var blobFolderPath = FilePath.GetApplicationTempDirectory() & "blobs";
        return new LocalFolderBlobStorage(new LocalFolderBlobStorage.Options {  BaseDirectory = blobFolderPath }, Services);
    }
}
