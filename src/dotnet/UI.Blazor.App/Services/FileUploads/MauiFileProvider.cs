using MemoryPack;

namespace ActualChat.UI.Blazor.App.Services;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class MauiFileProvider : IFileProvider
{
    private IServiceProvider _services = null!;

    [DataMember, MemoryPackOrder(0)]
    public string FileRef { get; init; } = "";
    [DataMember, MemoryPackOrder(1)]
    public string FileType { get; init; } = "";
    [DataMember, MemoryPackOrder(2)]
    public string FileName { get; init; } = "";

    private FileUploader Uploader => _services.GetRequiredService<FileUploader>();
    [field: AllowNull, MaybeNull]
    private IMauiFileProviderImpl Impl => field ??= _services.GetRequiredService<IMauiFileProviderImplFactory>().Create(FileRef);
    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= _services.LogFor<MauiFileProvider>();

    public void Initialize(IServiceProvider services)
        => _services = services;

    public Task<string> GetPreviewUrl()
        => Impl.GetPreviewUrl();

    public Task PrepareForSaving()
        => Impl.PrepareForSaving();

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", Justification = "Upload doesn't use reflection.")]
    public async Task<IFileUploadOperation> CreateUploadOperation(ChatId chatId)
    {
        var stream = await OpenRead().ConfigureAwait(false);
        if (stream is null)
            throw new InvalidOperationException("No file access.");
        var fileUploadOperation = Uploader.CreateUploadOperation(chatId, stream, FileType, FileName);
        _ = fileUploadOperation.Task.ContinueWith(async _ => {
            await fileUploadOperation.Task.SilentAwait(false);
            // NOTE: dispose stream when upload completed or canceled.
            await stream.DisposeSilentlyAsync().ConfigureAwait(false);
        }, TaskScheduler.Default);
        return fileUploadOperation;
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

    public Task ClearBeforeRemoving()
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
