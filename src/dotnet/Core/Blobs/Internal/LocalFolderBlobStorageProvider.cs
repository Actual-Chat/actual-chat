using Stl.IO;

namespace ActualChat.Blobs.Internal;

internal class LocalFolderBlobStorageProvider(IServiceProvider services) : IBlobStorageProvider
{
    private IServiceProvider Services { get; } = services;

    public IBlobStorage GetBlobStorage(Symbol blobScope)
    {
        var blobFolderPath = FilePath.GetApplicationDirectory() & "../blobs/";
        return new LocalFolderBlobStorage(new LocalFolderBlobStorage.Options {  BaseDirectory = blobFolderPath }, Services);
    }
}
