using ActualLab.IO;

namespace ActualChat.Blobs.Internal;

public class LocalFolderBlobStorages(IServiceProvider services) : IBlobStorages
{
    private IServiceProvider Services { get; } = services;

    public IBlobStorage this[Symbol blobScope] {
        get {
            var blobFolderPath = FilePath.GetApplicationDirectory() & "../blobs/";
            return new LocalFolderBlobStorage(
                new LocalFolderBlobStorage.Options { BaseDirectory = blobFolderPath },
                Services);
        }
    }
}
