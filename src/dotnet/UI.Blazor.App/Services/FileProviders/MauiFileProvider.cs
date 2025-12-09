using MemoryPack;

namespace ActualChat.UI.Blazor.App.Services;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class MauiFileProvider : IFileProvider
{
    private IServiceProvider _services = null!;

    [DataMember, MemoryPackOrder(0)]
    public FileMetadata Metadata { get; init; } = new ();
    [DataMember, MemoryPackOrder(1)]
    public string FileRef { get; init; } = "";

    private ChunkedFileUploader ChunkedFileUploader => _services.GetRequiredService<ChunkedFileUploader>();
    private IMauiFileProviderImpl Impl => field ??= _services.GetRequiredService<IMauiFileProviderImplFactory>().Create(FileRef);
    private ILogger Log => field ??= _services.LogFor<MauiFileProvider>();

    public void Initialize(IServiceProvider services)
        => _services = services;

    public Task<string> GetPreviewUrl()
        => Impl.GetPreviewUrl();

    public Task PrepareForSaving()
        => Impl.PrepareForSaving();

    public Task<Result<Unit>> UploadData(UploadId uploadId, IProgress<double> progressTracker, CancellationToken ct)
    {
        return ChunkedFileUploader.UploadData(uploadId, GetFile(), progressTracker, ct);

        async Task<Stream> GetFile()
        {
            var stream = await OpenRead().ConfigureAwait(false);
            return stream ?? throw StandardError.Internal("No file access.");
        }
    }

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
        => Task.CompletedTask;

    public Task<bool> WhenUserConsentGranted()
        => Task.FromResult(true);

    public Task ClearForRemoving()
        => Impl.ClearBeforeRemoving();

    private Task<Stream?> OpenRead()
        => Impl.OpenRead();
}

public interface IMauiFileProviderImplFactory
{
    IMauiFileProviderImpl Create(string fileRef);
}

public interface IMauiFileProviderImpl
{
    Task<string> GetPreviewUrl();
    Task PrepareForSaving();
    Task ClearBeforeRemoving();
    Task<Stream?> OpenRead();
}
