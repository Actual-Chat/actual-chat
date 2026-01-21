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

    public Task<bool> WhenUserConsentGranted()
    {
        if (WebFileProviderInternal is null)
            throw StandardError.Constraint("Can't call this method before CanAccessFile returns true.");

        return WebFileProviderInternal.WhenUserConsentGranted();
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
            if (jsRef is not null) {
                var whenUserConsentGranted = jsRef.InvokeAsync<bool>("whenUserConsentGranted", CancellationToken.None).AsTask();
                return new WebFileProviderInternal(jsRef, null, false, whenUserConsentGranted);
            }
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
            await WebFileProviderInternal.ClearForRemoving().ConfigureAwait(false);
            await WebFileProviderInternal.DisposeAsync().ConfigureAwait(false);
        }
        else
            await WebFileProviders.DeleteFileHandleFromDb(JS, FileHandleDbKey).ConfigureAwait(false);
    }

    public Task<string> GetPreviewUrl()
        => DemandWebFileProviderInternal().CreatePreviewUrl().AsTask();

    public Task WhenFileStreamReady()
        => DemandWebFileProviderInternal().WhenFileStreamReady();

    public Task UploadData(UploadId uploadId, IProgress<double> progressTracker, CancellationToken ct)
        => DemandWebFileProviderInternal().UploadData(uploadId, progressTracker, ct);

    private IWebFileProviderInternal DemandWebFileProviderInternal()
    {
        var @internal = WebFileProviderInternal;
        if (@internal is null)
            throw new InvalidOperationException("Upload can't be created.");
        return @internal;
    }
}

public interface IWebFileProviderInternal : IAsyncDisposable
{
    ValueTask<string> CreatePreviewUrl();
    ValueTask<string> SaveFileHandleToDb();
    Task UploadData(UploadId uploadId, IProgress<double> progressTracker, CancellationToken ct);
    Task<bool> WhenUserConsentGranted();
    Task WhenFileStreamReady();
    Task ClearForRemoving();
}

public class WebFileProviderInternal : IWebFileProviderInternal
{
    private readonly IJSObjectReference _jsRef;
    private bool _disposed;
    private string? _previewUrl;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly CancellationToken _cancellationToken;
    private readonly Task<bool> _whenUserConsentGranted;

    public bool IsOriginal { get; }

    public WebFileProviderInternal(
        IJSObjectReference jsRef,
        string? previewUrl,
        bool isOriginal,
        Task<bool> whenUserConsentGranted)
    {
        _jsRef = jsRef;
        _previewUrl = previewUrl;
        _cancellationTokenSource = new CancellationTokenSource();
        _cancellationToken = _cancellationTokenSource.Token;
        _whenUserConsentGranted = whenUserConsentGranted;
        IsOriginal = isOriginal;
    }

    public Task<bool> WhenUserConsentGranted() => _whenUserConsentGranted;

    public async ValueTask<string> CreatePreviewUrl()
    {
        _previewUrl ??= await _jsRef.InvokeAsync<string>("createPreviewUrl", _cancellationToken).ConfigureAwait(false);
        return _previewUrl;
    }

    public async Task ClearForRemoving()
    {
        await _jsRef.InvokeVoidAsync("clearForRemoving", _cancellationToken).ConfigureAwait(false);
        _previewUrl = null;
    }

    public ValueTask<string> SaveFileHandleToDb()
        => _jsRef.InvokeAsync<string>("saveFileHandleToDb", _cancellationToken);

    public async Task UploadData(UploadId uploadId, IProgress<double> progressTracker, CancellationToken ct)
    {
        var cts = _cancellationToken.LinkWith(ct);
        var ct2 = cts.Token;
        var backend = new WebFileUploaderBackend(progressTracker);
        var blazorRef = backend.BlazorRef;
        try {
            ct.Register(() => {
                _ = Cancel();
            });
            // Upload data
            await Start(uploadId, blazorRef, ct2).ConfigureAwait(false);
            await backend.WhenUploadCompleted.WaitAsync(ct2).ConfigureAwait(false);
        }
        finally {
            blazorRef.Dispose();
            cts.DisposeSilently();
        }
    }

    public async Task WhenFileStreamReady()
    {
        var granted = await WhenUserConsentGranted().ConfigureAwait(false);
        if (granted)
            return;

        await TaskExt.NeverEnding(_cancellationToken).ConfigureAwait(false);
    }

    private ValueTask Start(UploadId uploadId, DotNetObjectReference<IWebFileUploaderBackend> blazorRef, CancellationToken ct)
        => _jsRef.InvokeVoidAsync("start", ct, uploadId.Value, blazorRef);

    private ValueTask Cancel()
        => _jsRef.InvokeVoidAsync("cancel", _cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cancellationTokenSource.CancelAndDisposeSilently();
        await _jsRef.DisposeSilentlyAsync().ConfigureAwait(false);
    }
}

public class NoFileAccessWebFileProviderInternal(IJSRuntime jsRuntime, string fileHandleDbKey) : IWebFileProviderInternal
{
    public ValueTask<string> CreatePreviewUrl()
        => throw new NotSupportedException();

    public ValueTask<string> SaveFileHandleToDb()
        => throw new NotSupportedException();

    public Task ClearForRemoving()
        => WebFileProviders.DeleteFileHandleFromDb(jsRuntime, fileHandleDbKey).AsTask();

    public Task UploadData(UploadId uploadId, IProgress<double> progressTracker, CancellationToken ct)
        => throw new NotSupportedException();

    Task<bool> IWebFileProviderInternal.WhenUserConsentGranted()
        => throw new NotSupportedException();

    public Task WhenFileStreamReady()
        => throw new NotSupportedException();

    public ValueTask DisposeAsync()
        => ValueTask.CompletedTask;
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
