using ActualChat.UI.Services;
using ActualLab.IO;

namespace ActualChat.UI.Blazor.App.Services;

[DataContract, MessagePackObject]
public partial class MauiFileProvider : IFileProvider
{
    private IServiceProvider _services = null!;

    [DataMember, Key(0)]
    public FileMetadata Metadata { get; init; } = new ();
    [DataMember, Key(1)]
    public FilePath FileRef { get; init; } = "";

    private IMauiFileProviderImpl Impl => field ??= _services.GetRequiredService<IMauiFileProviderImplFactory>().
        Create(FileRef);
    private ILogger Log => field ??= _services.LogFor<MauiFileProvider>();

    public void Initialize(IServiceProvider services)
        => _services = services;

    public Task<FilePreview> GetPreview(CancellationToken cancellationToken = default)
        => Impl.GetPreview(cancellationToken);

    public Task PrepareForSaving()
        => Impl.PrepareForSaving();

    public async Task<bool> CheckAccess()
    {
        try {
            var stream = await OpenRead().ConfigureAwait(false);
            if (stream is null)
                return false;

            await using (stream.ConfigureAwait(false)) { }
            return true;
        }
        catch(Exception ex) {
            Log.LogWarning(ex, "Failed to open file for read. File ref: '{FileRef}'", FileRef);
            return false;
        }
    }

    public Task WhenFileStreamReady()
        => Impl.WhenFileStreamReady();

    public UploadSource GetUploadSource()
    {
        // Read actual file size from disk if not set in metadata
        var length = Metadata.Length > 0 ? Metadata.Length : FileRef.FileSize;
        var metadata = new UploadSourceMetadata(
            Metadata.FileType,
            length,
            Metadata.FileName);
        return new UploadSource(metadata, new StreamUploadSource(GetFile));

        async Task<Stream> GetFile()
        {
            var stream = await OpenRead().ConfigureAwait(false);
            return stream ?? throw StandardError.Internal("No file access.");
        }
    }

    public Task<bool> WhenUserConsentGranted()
        => Task.FromResult(true);

    public Task ClearForRemoving()
        => Impl.ClearBeforeRemoving();

    private Task<Stream?> OpenRead()
        => Impl.OpenRead();
}

public interface IMauiFileProviderImplFactory
{
    IMauiFileProviderImpl Create(FilePath fileRef);
}

public interface IMauiFileProviderImpl
{
    Task WhenFileStreamReady();
    Task<FilePreview> GetPreview(CancellationToken cancellationToken = default);
    Task PrepareForSaving();
    Task ClearBeforeRemoving();
    Task<Stream?> OpenRead();
}
