using ActualChat.Module;
using ActualLab.IO;

namespace ActualChat.Blobs.Internal;

public class TempFolderBlobStorages(IServiceProvider services) : IBlobStorages
{
    private FilePath? _blobsDir;

    private IServiceProvider Services { get; } = services;
    private CoreSettings Settings => field ??= Services.GetRequiredService<CoreSettings>();
    private HostInfo HostInfo => field ??= Services.GetRequiredService<HostInfo>();
    private FilePath BlobsDir => _blobsDir ??= BuildBlobsDir();

    public IBlobStorage this[Symbol blobScope]
        => new LocalFolderBlobStorage(new () { BaseDirectory = BlobsDir }, Services);

    private FilePath BuildBlobsDir()
    {
        var dir = FilePath.GetApplicationTempDirectory();
        if (!HostInfo.IsTested)
            return dir & "blobs";

        return dir & $"tst-{Settings.Instance}" & "blobs";
    }
}
