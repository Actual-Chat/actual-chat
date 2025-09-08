using ActualChat.UI.Blazor.App.Module;
using MemoryPack;

namespace ActualChat.UI.Blazor.App.Services;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class WebFileProvider : IFileProvider
{
    private static readonly string JSCreateMethod = $"{BlazorUIAppModule.ImportName}.WebFileProviders.tryCreateFromFileHandleDbKey";

    [DataMember, MemoryPackOrder(0)]
    public string FileName { get; init; } = "";
    [DataMember, MemoryPackOrder(1)]
    public long FileSize { get; init; }
    [DataMember, MemoryPackOrder(2)]
    public string FileHandleDbKey { get; set; } = "";
    [IgnoreDataMember, MemoryPackIgnore]
    public bool IsOriginal => WebFileProviderInternal is not null && WebFileProviderInternal.IsOriginal;

    [IgnoreDataMember, MemoryPackIgnore]
    public WebFileProviderInternal? WebFileProviderInternal { get; set; }

    public async Task<bool> CheckAccess(UploadSessionContext context)
    {
        if (IsOriginal)
            return true;

        if (FileHandleDbKey.IsNullOrEmpty())
            return false;

        var js = context.Services.JSRuntime();
        try {
            CancellationToken cancellationToken = default;
            var chatId = context.Session.ChatId;
            var backend = new FileUploaderBackend();
            var jsRef = await js
                .InvokeAsync<IJSObjectReference?>(JSCreateMethod,
                    cancellationToken,
                    FileHandleDbKey,
                    chatId,
                    backend.BlazorRef)
                .ConfigureAwait(false);
            if (jsRef is null)
                return false;

            WebFileProviderInternal = new WebFileProviderInternal(jsRef, backend, false);
            return true;
        }
        catch (Exception ex) {
            return false;
        }
    }

    public async Task PrepareForSaving()
    {
        if (!IsOriginal)
            return;

        if (WebFileProviderInternal is null)
            return;

        FileHandleDbKey = await WebFileProviderInternal.SaveFileHandleToDb().ConfigureAwait(false);
    }

    public async Task ClearBeforeRemoving()
    {
        if (FileHandleDbKey.IsNullOrEmpty())
            return;

        if (WebFileProviderInternal is null)
            return;

        await WebFileProviderInternal.RemoveFileHandleFromDb().ConfigureAwait(false);
    }

    public Task<IFileUploadOperation> CreateUploadOperation()
    {
        var @internal = WebFileProviderInternal;
        if (@internal is null)
            throw new InvalidOperationException("Upload can't be created.");

        var uploadOperation = new FileUploadOperation(async ct => {
            ct.Register(() => {
                @internal.Tracker.SetCanceled();
                _ = @internal.Cancel();
            });
            await @internal.Start().ConfigureAwait(false);
            return await @internal.Tracker.Task.ConfigureAwait(false);
        }) {
            Progress = @internal.Tracker.Progress,
        };
        return Task.FromResult<IFileUploadOperation>(uploadOperation);
    }
}

public class WebFileProviderInternal
{
    private readonly IJSObjectReference _jsRef;

    public FileUploaderTracker Tracker => Backend.Tracker;
    public FileUploaderBackend Backend { get; }
    public bool IsOriginal { get; }

    public WebFileProviderInternal(
        IJSObjectReference jsRef,
        FileUploaderBackend backend,
        bool isOriginal)
    {
        _jsRef = jsRef;
        Backend = backend;
        IsOriginal = isOriginal;
    }

    public ValueTask Start()
        => _jsRef.InvokeVoidAsync("start");

    public ValueTask Cancel()
        => _jsRef.InvokeVoidAsync("cancel");

    public ValueTask<string> SaveFileHandleToDb()
        => _jsRef.InvokeAsync<string>("saveFileHandleToDb");

    public ValueTask<string> RemoveFileHandleFromDb()
        => _jsRef.InvokeAsync<string>("removeFileHandleFromDb");
}
