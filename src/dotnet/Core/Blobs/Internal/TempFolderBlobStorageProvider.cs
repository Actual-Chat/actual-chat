using Microsoft.AspNetCore.StaticFiles;
using Stl.IO;

namespace ActualChat.Blobs.Internal;

internal class TempFolderBlobStorageProvider : IBlobStorageProvider
{
    private IServiceProvider Services { get; }

    public TempFolderBlobStorageProvider(IServiceProvider services)
        => Services = services;

    public IBlobStorage GetBlobStorage(Symbol blobScope)
    {
        var blobFolderPath = FilePath.GetApplicationTempDirectory() & "blobs";
        return new LocalFolderBlobStorage(blobFolderPath, Services);
    }
}
