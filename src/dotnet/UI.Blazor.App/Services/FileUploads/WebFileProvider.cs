using ActualChat.UI.Blazor.App.Module;
using MemoryPack;

namespace ActualChat.UI.Blazor.App.Services;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class WebFileProvider : IFileProvider
{
    private static readonly string JSCreateMethod = $"{BlazorUIAppModule.ImportName}.WebFileProviders.tryCreateFromFileHandleDbKey";
    private IServiceProvider? _services;

    [DataMember, MemoryPackOrder(0)]
    public string FileName { get; init; } = "";
    [DataMember, MemoryPackOrder(1)]
    public string FileHandleDbKey { get; set; } = "";
    [DataMember, MemoryPackOrder(2)]
    public ChatId ChatId { get; set; } = null!;
    [IgnoreDataMember, MemoryPackIgnore]
    public IWebFileProviderInternal? WebFileProviderInternal { get; set; }
    [IgnoreDataMember, MemoryPackIgnore]
    private bool IsOriginal => WebFileProviderInternal is WebFileProviderInternal provider && provider.IsOriginal;
    [IgnoreDataMember, MemoryPackIgnore]
    private IServiceProvider Services => _services ?? throw new InvalidOperationException("Initialize must be called first.");
    [field: AllowNull, MaybeNull]
    private IJSRuntime JS => field ??= Services.JSRuntime();
    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= Services.LogFor<WebFileProvider>();

    public void Initialize(IServiceProvider services)
        => _services = services;

    public async Task<bool> CheckAccess()
    {
        WebFileProviderInternal ??= await CreateInternal().ConfigureAwait(false);
        return WebFileProviderInternal is WebFileProviderInternal;
    }

    private async Task<IWebFileProviderInternal> CreateInternal()
    {
        var js = JS;
        if (FileHandleDbKey.IsNullOrEmpty())
            return new NoFileAccessWebFileProviderInternal(js, "");

        var backend = new FileUploaderBackend();
        try {
            CancellationToken cancellationToken = default;
            var nullableRef = await js
                .InvokeAsync<NullableJSObjectReference>(JSCreateMethod,
                    cancellationToken,
                    FileHandleDbKey,
                    ChatId,
                    backend.BlazorRef)
                .ConfigureAwait(false);
            var jsRef = nullableRef.Value;
            if (jsRef is not null)
                return new WebFileProviderInternal(jsRef, backend, false);
        }
        catch (Exception ex) {
            Log.LogWarning(ex, "Failed to create WebFileProviderInternal");
            backend.BlazorRef.DisposeSilently();
        }
        return new NoFileAccessWebFileProviderInternal(js, FileHandleDbKey);
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
        if (WebFileProviderInternal is not null) {
            await WebFileProviderInternal.RevokePreviewUrl().ConfigureAwait(false);
            await WebFileProviderInternal.DeleteFileHandleFromDb().ConfigureAwait(false);
            return;
        }

        await WebFileProviders.DeleteFileHandleFromDb(JS, FileHandleDbKey).ConfigureAwait(false);
    }

    public Task<string> GetPreviewUrl()
    {
        var @internal = WebFileProviderInternal;
        if (@internal is null)
            throw new InvalidOperationException("Upload can't be created.");
        return @internal.CreatePreviewUrl().AsTask();
    }

    public Task<IFileUploadOperation> CreateUploadOperation()
    {
        var @internal = WebFileProviderInternal;
        if (@internal is null)
            throw new InvalidOperationException("Upload can't be created.");

        return @internal.CreateUploadOperation();
    }
}

public interface IWebFileProviderInternal
{
    ValueTask<string> CreatePreviewUrl();
    ValueTask RevokePreviewUrl();
    ValueTask<string> SaveFileHandleToDb();
    ValueTask<bool> DeleteFileHandleFromDb();
    Task<IFileUploadOperation> CreateUploadOperation();
}

public class WebFileProviderInternal : IWebFileProviderInternal
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

    public ValueTask<string> CreatePreviewUrl()
        => _jsRef.InvokeAsync<string>("createPreviewUrl");

    public ValueTask RevokePreviewUrl()
        => _jsRef.InvokeVoidAsync("revokePreviewUrl");

    public ValueTask<string> SaveFileHandleToDb()
        => _jsRef.InvokeAsync<string>("saveFileHandleToDb");

    public ValueTask<bool> DeleteFileHandleFromDb()
        => _jsRef.InvokeAsync<bool>("removeFileHandleFromDb");

    public Task<IFileUploadOperation> CreateUploadOperation()
    {
        var uploadOperation = new FileUploadOperation(async ct => {
            ct.Register(() => {
                Tracker.SetCanceled();
                _ = Cancel();
            });
            await Start().ConfigureAwait(false);
            return await Tracker.Task.ConfigureAwait(false);
        }) {
            Progress = Tracker.Progress,
        };
        return Task.FromResult<IFileUploadOperation>(uploadOperation);
    }

    private ValueTask Start()
        => _jsRef.InvokeVoidAsync("start");

    private ValueTask Cancel()
        => _jsRef.InvokeVoidAsync("cancel");
}

public class NoFileAccessWebFileProviderInternal(IJSRuntime jsRuntime, string fileHandleDbKey) : IWebFileProviderInternal
{
    public ValueTask<string> CreatePreviewUrl()
        => throw new NotImplementedException();

    public ValueTask RevokePreviewUrl()
        => throw new NotSupportedException();

    public ValueTask<string> SaveFileHandleToDb()
        => throw new NotSupportedException();

    public ValueTask<bool> DeleteFileHandleFromDb()
        => WebFileProviders.DeleteFileHandleFromDb(jsRuntime, fileHandleDbKey);

    public Task<IFileUploadOperation> CreateUploadOperation()
        => throw new NotSupportedException();
}

internal static class WebFileProviders
{
    private static readonly string JSDeleteMethod = $"{BlazorUIAppModule.ImportName}.WebFileProviders.deleteFileHandleFromDb";

    public static async ValueTask<bool> DeleteFileHandleFromDb(IJSRuntime jsRuntime, string fileHandleDbKey)
    {
        if (fileHandleDbKey.IsNullOrEmpty())
            return false;

        await jsRuntime.InvokeVoidAsync(JSDeleteMethod, fileHandleDbKey).ConfigureAwait(false);
        return true;
    }
}
