using MemoryPack;

namespace ActualChat.UI.Blazor.App.Services;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class LocalFileProvider : IFileProvider
{
    private IServiceProvider _services = null!;

    [DataMember, MemoryPackOrder(0)]
    public string FilePath { get; init; } = "";
    [DataMember, MemoryPackOrder(1)]
    public string FileType { get; init; } = "";
    [DataMember, MemoryPackOrder(2)]
    public ChatId ChatId { get; set; } = null!;
    [field: AllowNull, MaybeNull]
    private FileInfo FileInfo => field ??= new FileInfo(FilePath);

    [IgnoreDataMember, MemoryPackIgnore]
    public long FileSize => FileInfo.Length;
    [IgnoreDataMember, MemoryPackIgnore]
    public string FileName => FileInfo.Name;

    private FileUploader Uploader => _services.GetRequiredService<FileUploader>();

    public void Initialize(IServiceProvider services)
        => this._services = services;

    public Task PrepareForSaving()
        => Task.CompletedTask;

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", Justification = "Upload doesn't use reflection.")]
    public async Task<IFileUploadOperation> CreateUploadOperation()
    {
        var progress = new Progress<double>();
        var stream = await OpenReadAsync().ConfigureAwait(false);
        var fileUploadOperation = Uploader.CreateUploadOperation(ChatId, stream, FileType, FileName, progress);
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
            var stream = await OpenReadAsync(0).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false)) { }
            return true;
        }
        catch {
            Console.WriteLine($"Файл для сессии {FileName} недоступен");
            return false;
        }
    }

    public Task ClearBeforeRemoving()
        => Task.CompletedTask;

    public Task<string> GetPreviewUrl()
        => Task.FromResult(ContentResolver.GetFileUri(FilePath));

    private Task<Stream> OpenReadAsync(long offset = 0)
    {
        var stream = FileInfo.OpenRead();
        stream.Seek(offset, SeekOrigin.Begin);
        return Task.FromResult<Stream>(stream);
    }
}
