using MemoryPack;

namespace ActualChat.UI.Blazor.App.Services;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class IncomingShareFileProvider : IFileProvider
{
    private IServiceProvider _services = null!;

    [DataMember, MemoryPackOrder(0)]
    public string FakeField { get; init; } = "";
    [IgnoreDataMember, MemoryPackIgnore]
    public string FileName { get; init; } = "";
    [IgnoreDataMember, MemoryPackIgnore]
    public string FileUrl { get; init; } = "";
    [IgnoreDataMember, MemoryPackIgnore]
    public string? MediaType { get; init; } = "";
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId? ChatId { get; init; }
    [IgnoreDataMember, MemoryPackIgnore]
    public Stream? Stream { get; init; }

    private FileUploader Uploader => _services.GetRequiredService<FileUploader>();

    public void Initialize(IServiceProvider services)
        => this._services = services;

    public Task PrepareForSaving()
        // Do nothing. The Provider does not support recovery after the app restarts. Hence, no need to save anything.
        => Task.CompletedTask;

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", Justification = "Upload doesn't use reflection.")]
    public Task<IFileUploadOperation> CreateUploadOperation()
    {
        var progress = new Progress<double>();
        var fileUploadOperation = Uploader.CreateUploadOperation(ChatId, Stream, MediaType, FileName, progress);
        _ = fileUploadOperation.Task.ContinueWith(async _ => {
            await fileUploadOperation.Task.SilentAwait(false);
            // NOTE: dispose stream when upload completed or canceled.
            await Stream.DisposeSilentlyAsync().ConfigureAwait(false);
        }, TaskScheduler.Default);
        return Task.FromResult<IFileUploadOperation>(fileUploadOperation);
    }

    public Task<bool> CheckAccess()
        => Task.FromResult(false); // Does not support recovery after app restart.

    public Task ClearBeforeRemoving()
        => Task.CompletedTask;

    public Task<string> GetPreviewUrl()
        => Task.FromResult(string.Empty);
}
