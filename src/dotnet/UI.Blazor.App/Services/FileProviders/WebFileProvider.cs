using ActualChat.Media;
using ActualChat.UI.Blazor.App.Module;
using MemoryPack;

namespace ActualChat.UI.Blazor.App.Services;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class WebFileProvider : IFileProvider
{
    private static readonly string JSCreateMethod = $"{BlazorUIAppModule.ImportName}.WebFileProviders.tryCreateFromFileHandleDbKey";
    private IServiceProvider? _services;

    [DataMember, MemoryPackOrder(0)]
    public FileMetadata Metadata { get; init; } = new ();
    [DataMember, MemoryPackOrder(1)]
    public string FileHandleDbKey { get; set; } = "";

    [IgnoreDataMember, MemoryPackIgnore]
    public IWebFileProviderInternal? WebFileProviderInternal { get; set; }
    [IgnoreDataMember, MemoryPackIgnore]
    private bool IsOriginal => WebFileProviderInternal is WebFileProviderInternal provider && provider.IsOriginal;
    [IgnoreDataMember, MemoryPackIgnore]
    private IServiceProvider Services => _services ?? throw new InvalidOperationException("Initialize must be called first.");
    private IJSRuntime JS => field ??= Services.JSRuntime();
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

        try {
            CancellationToken cancellationToken = default;
            var nullableRef = await js
                .InvokeAsync<NullableJSObjectReference>(JSCreateMethod,
                    cancellationToken,
                    FileHandleDbKey)
                .ConfigureAwait(false);
            var jsRef = nullableRef.Value;
            if (jsRef is not null)
                return new WebFileProviderInternal(_services.Require().AppUIHub(), jsRef, null, false);
        }
        catch (Exception ex) {
            Log.LogWarning(ex, "Failed to create WebFileProviderInternal");
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

    public async Task ClearForRemoving()
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

    public Task<IFileUploadOperation> CreateUploadOperation(UploadId uploadId)
    {
        var @internal = WebFileProviderInternal;
        if (@internal is null)
            throw new InvalidOperationException("Upload can't be created.");

        return @internal.CreateUploadOperation(uploadId);
    }
}

public interface IWebFileProviderInternal
{
    ValueTask<string> CreatePreviewUrl();
    ValueTask RevokePreviewUrl();
    ValueTask<string> SaveFileHandleToDb();
    ValueTask<bool> DeleteFileHandleFromDb();
    Task<IFileUploadOperation> CreateUploadOperation(UploadId uploadId);
}

public class WebFileProviderInternal : IWebFileProviderInternal, IAsyncDisposable
{
    private readonly AppUIHub _hub;
    private readonly IJSObjectReference _jsRef;
    private readonly List<IDisposable> _disposables = new ();
    private bool _disposed;
    private string? _previewUrl;

    public bool IsOriginal { get; }

    public WebFileProviderInternal(
        AppUIHub hub,
        IJSObjectReference jsRef,
        string? previewUrl,
        bool isOriginal)
    {
        _hub = hub;
        _jsRef = jsRef;
        _previewUrl = previewUrl;
        IsOriginal = isOriginal;
    }

    public async ValueTask<string> CreatePreviewUrl()
    {
        _previewUrl ??= await _jsRef.InvokeAsync<string>("createPreviewUrl").ConfigureAwait(false);
        return _previewUrl;
    }

    public async ValueTask RevokePreviewUrl()
    {
        if (_previewUrl is null)
            return;

        await _jsRef.InvokeVoidAsync("revokePreviewUrl").ConfigureAwait(false);
        _previewUrl = null;
    }

    public ValueTask<string> SaveFileHandleToDb()
        => _jsRef.InvokeAsync<string>("saveFileHandleToDb");

    public ValueTask<bool> DeleteFileHandleFromDb()
        => _jsRef.InvokeAsync<bool>("removeFileHandleFromDb");

    public Task<IFileUploadOperation> CreateUploadOperation(UploadId uploadId)
    {
        var fileUploaderBackend = new WebFileUploaderBackend();
        _disposables.Add(fileUploaderBackend.BlazorRef);
        var tracker = fileUploaderBackend.Tracker;
        var uploadOperation = new FileUploadOperation(async ct => {
            ct.Register(() => {
                tracker.SetCanceled();
                _ = Cancel();
            });
            // Upload data
            await Start(uploadId, fileUploaderBackend.BlazorRef).ConfigureAwait(false);
            await fileUploaderBackend.WhenUploadCompleted.WaitAsync(ct).ConfigureAwait(false);
            // Convert uploaded file to media content
            var mediaContent = await _hub.Commander.Call(new Uploads_Complete(_hub.Session, uploadId), ct).ConfigureAwait(false);
            tracker.SetResult(mediaContent);
            return mediaContent;
        }, tracker);
        return Task.FromResult<IFileUploadOperation>(uploadOperation);
    }

    private ValueTask Start(UploadId uploadId, DotNetObjectReference<IWebFileUploaderBackend> blazorRef)
        => _jsRef.InvokeVoidAsync("start", uploadId.Value, Constants.Uploads.DefaultChunkSize, blazorRef);

    private ValueTask Cancel()
        => _jsRef.InvokeVoidAsync("cancel");

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await _jsRef.DisposeAsync().ConfigureAwait(false);
        foreach (var disposable in _disposables)
            disposable.Dispose();
        _disposables.Clear();
    }
}

public class NoFileAccessWebFileProviderInternal(IJSRuntime jsRuntime, string fileHandleDbKey) : IWebFileProviderInternal
{
    public ValueTask<string> CreatePreviewUrl()
        => throw new NotSupportedException();

    public ValueTask RevokePreviewUrl()
        => ValueTask.CompletedTask;

    public ValueTask<string> SaveFileHandleToDb()
        => throw new NotSupportedException();

    public ValueTask<bool> DeleteFileHandleFromDb()
        => WebFileProviders.DeleteFileHandleFromDb(jsRuntime, fileHandleDbKey);

    public Task<IFileUploadOperation> CreateUploadOperation(UploadId uploadId)
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
