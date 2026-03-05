using ActualChat.UI.Blazor.App.Services;
using ActualLab.Diagnostics;
using ActualLab.IO;

namespace ActualChat.App.Maui;

public sealed class IosFileProviderImpl : WorkerBase, IMauiFileProviderImpl
{
    private readonly IServiceProvider _services;
    private readonly FilePath _filePath;
    private readonly FilePath _transientFilePath;
    private readonly TaskCompletionSource _whenCopied = TaskCompletionSourceExt.New();

    private FilePath TempFilePath => _filePath + ".tmp";

    private IosVideoThumbnails VideoThumbnails => field ??= _services.GetRequiredService<IosVideoThumbnails>();
    private ILogger Log => field ??= _services.LogFor(GetType());
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.FileAttachments);

    public IosFileProviderImpl(IServiceProvider services, FilePath filePath, FilePath transientFilePath)
    {
        _services = services;
        _filePath = filePath;
        _transientFilePath = transientFilePath;

        if (File.Exists(filePath))
            // File already fully copied
            _whenCopied.TrySetResult();
        else if (!transientFilePath.IsEmpty && File.Exists(transientFilePath))
            // Fresh pick - start background copy
            this.Start();
    }

    protected override Task OnRun(CancellationToken cancellationToken)
        => AsyncChain.From(CopyFile)
            .LogError(Log)
            .Run(cancellationToken);

    private async Task CopyFile(CancellationToken cancellationToken)
    {
        try {
            var copyStartedAt = CpuTimestamp.Now;
            // Copy to temp file first
            await _transientFilePath.CopyTo(TempFilePath, cancellationToken).ConfigureAwait(false);
            // Rename to final name (atomic operation)
            File.Move(TempFilePath, _filePath);
            Log.LogInformation(
                "Copied picked media '{FileName}' ({Size} bytes) in {Elapsed}",
                _filePath.FileName, _filePath.FileSize, copyStartedAt.Elapsed.ToShortString());
            _whenCopied.TrySetResult();
        }
        catch (Exception e) {
            _whenCopied.TrySetException(e);
            throw;
        }
    }

    public async Task<FilePreview> GetPreview(CancellationToken cancellationToken = default)
    {
        // Use transient file path for preview if copy is not complete yet
        var previewPath = _transientFilePath.IsEmpty || _whenCopied.Task.IsCompletedSuccessfully
            ? _filePath
            : _transientFilePath;

        if (!OrdinalIgnoreCaseEquals(previewPath.Extension, ".mov"))
            return new FilePreview(ContentResolver.GetFileUri(previewPath));

        var thumbnail = await VideoThumbnails.Generate(previewPath, cancellationToken).ConfigureAwait(false);
        DebugLog?.LogDebug("Generated thumbnail: {ThumbnailPath}", thumbnail?.Path);
        return thumbnail is { } t
            ? new FilePreview(ContentResolver.GetFileUri(t.Path), t.Size)
            : new FilePreview(ContentResolver.GetFileUri(previewPath));
    }

    public Task WhenFileStreamReady()
        => _whenCopied.Task;

    public Task PrepareForSaving()
        => Task.CompletedTask;

    public Task ClearBeforeRemoving()
    {
        File.Delete(_filePath);
        File.Delete(TempFilePath);
        return Task.CompletedTask;
    }

    public async Task<Stream?> OpenRead()
    {
        if (!_transientFilePath.IsEmpty)
            await _whenCopied.Task.ConfigureAwait(false);

        var fileInfo = new FileInfo(_filePath);
        return fileInfo.Exists ? fileInfo.OpenRead() : null;
    }

}
