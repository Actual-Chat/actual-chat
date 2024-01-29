using ActualLab.IO;

namespace ActualChat.Blobs.Internal;

public class TempFolderBlobStorages(IServiceProvider services) : IBlobStorages
{
    private IServiceProvider Services { get; } = services;

    public IBlobStorage this[Symbol blobScope] {
        get {
            var blobFolderPath = FilePath.GetApplicationTempDirectory() & "blobs";
            return new LocalFolderBlobStorage(
                new LocalFolderBlobStorage.Options { BaseDirectory = blobFolderPath },
                Services);
        }
    }
}
