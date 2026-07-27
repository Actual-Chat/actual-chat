using Microsoft.AspNetCore.StaticFiles;
using ActualLab.IO;

namespace ActualChat.Blobs.Internal;

public class LocalFolderBlobStorage(LocalFolderBlobStorage.Options options, IServiceProvider services)
    : IBlobStorage
{
    public record Options
    {
        public FilePath BaseDirectory { get; init; } = ".";
    }

    private FilePath BaseDirectory { get; } = options.BaseDirectory.FullPath;
    private IServiceProvider Services { get; } = services;
    private ILogger Log { get; } = services.LogFor<LocalFolderBlobStorage>();
    private IContentTypeProvider ContentTypeProvider => field ??= Services.GetRequiredService<IContentTypeProvider>();

    public ValueTask DisposeAsync()
        => default;

    public Task<bool> Exists(string path, CancellationToken cancellationToken)
    {
        var fullPath = GetFullPath(path);
        if (File.Exists(fullPath))
            return ActualLab.Async.TaskExt.TrueTask;
        if (Directory.Exists(fullPath))
            return ActualLab.Async.TaskExt.TrueTask;

        return ActualLab.Async.TaskExt.FalseTask;
    }

    public Task<Stream?> Read(string path, CancellationToken cancellationToken)
    {
        var fullPath = GetFullPath(path);

        try {
            return Task.FromResult<Stream?>(File.OpenRead(fullPath));
        }
        catch (DirectoryNotFoundException) {
            return Task.FromResult<Stream?>(null);
        }
        catch (FileNotFoundException) {
            return Task.FromResult<Stream?>(null);
        }
    }

    public Task<string?> GetContentType(string path, CancellationToken cancellationToken)
    {
        var fullPath = GetFullPath(path);
        return File.Exists(fullPath) && ContentTypeProvider.TryGetContentType(fullPath, out var contentType)
            ? Task.FromResult<string?>(contentType)
            : Task.FromResult<string?>(null);
    }

    public async Task Write(string path, Stream stream, string contentType, CancellationToken cancellationToken)
    {
        var fullPath = GetFullPath(path);
        Directory.CreateDirectory(fullPath.DirectoryPath);

        if (File.Exists(fullPath))
            return; // already written

        try {
            var fileStream = new FileStream(fullPath, FileMode.CreateNew);
            await using var _ = fileStream.ConfigureAwait(false);
            await stream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException e) {
            Log.LogWarning(e, "Error writing blob file");
            // already exists
        }
    }

    public Task Copy(string oldPath, string newPath, CancellationToken cancellationToken)
    {
        var fullOldPath = GetFullPath(oldPath);
        var fullNewPath = GetFullPath(newPath);

        Directory.CreateDirectory(fullNewPath.DirectoryPath);

        File.Copy(fullOldPath, fullNewPath);

        return Task.CompletedTask;
    }

    public Task Delete(string path, CancellationToken cancellationToken)
    {
        var fullPath = GetFullPath(path);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        else if (Directory.Exists(fullPath))
            Directory.Delete(fullPath, true);
        else
            return Task.FromException(StandardError.Constraint($"Cannot delete. No such object: '{path}'."));

        return Task.CompletedTask;
    }

    public async Task Append(string path, Stream stream, CancellationToken cancellationToken)
    {
        var fullPath = GetFullPath(path);
        if (!File.Exists(fullPath))
            throw StandardError.Constraint($"Cannot append to non-existent file: '{path}'.");

        var fileStream = new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.None);
        await using var _ = fileStream.ConfigureAwait(false);
        await stream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private FilePath GetFullPath(string path)
        => FilePathValidator.GetContainedPath(BaseDirectory, path);
}
